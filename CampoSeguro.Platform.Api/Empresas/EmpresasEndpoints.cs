using System.Security.Claims;
using CampoSeguro.Data;
using CampoSeguro.Data.Configuracoes;
using CampoSeguro.Data.Seguranca;
using CampoSeguro.Domain;
using CampoSeguro.Platform.Api.Autenticacao;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CampoSeguro.Platform.Api.Empresas;

/// <summary>
/// Cadastro das empresas-clientes e dos gestores delas. A Plataforma só cria gestores: técnicos
/// são cadastrados pelo próprio gestor no Painel. Sem serviço de e-mail ainda, o link do convite
/// volta na resposta para o administrador repassar — o mesmo caminho oficial que o Painel terá.
/// </summary>
public static class EmpresasEndpoints
{
    public static void MapEmpresas(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/empresas").WithTags("Empresas");

        grupo.MapGet("/", Listar);
        grupo.MapGet("/{id:guid}", Detalhar);
        grupo.MapPost("/", Criar);
        grupo.MapPost("/{id:guid}/gestores", ConvidarGestor);
        grupo.MapPost("/{id:guid}/gestores/{gestorId:guid}/convite", ReenviarConvite);
    }

    private static async Task<IResult> Listar(CampoSeguroDbContext db, CancellationToken ct)
    {
        var empresas = await db.Tenants.AsNoTracking().OrderBy(t => t.Nome).ToListAsync(ct);
        return Results.Ok(empresas.Select(ResumoEmpresa.De));
    }

    private static async Task<IResult> Detalhar(Guid id, CampoSeguroDbContext db, CancellationToken ct)
    {
        var empresa = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (empresa is null)
            return EmpresaNaoEncontrada();

        var gestores = await db.DeTodosOsTenants<Usuario>().AsNoTracking()
            .Where(u => u.TenantId == id && u.Papel == Papel.Gestor)
            .OrderBy(u => u.Nome)
            .ToListAsync(ct);

        var ids = gestores.Select(g => g.Id).ToList();
        var convites = await ConvitesPendentes(db, ids).AsNoTracking().ToListAsync(ct);

        return Results.Ok(new DetalheEmpresa(
            empresa.Id, empresa.Nome, empresa.Documento, empresa.SituacaoAssinatura, empresa.CriadoEm,
            gestores.Select(g => Resumo(g, convites.SingleOrDefault(c => c.UsuarioId == g.Id))).ToList()));
    }

    /// <summary>Empresa, primeiro gestor e convite numa única transação: não existe empresa sem gestor.</summary>
    private static async Task<IResult> Criar(NovaEmpresa pedido, CampoSeguroDbContext db, OpcoesConvite opcoes,
        ClaimsPrincipal usuario, ILoggerFactory logs, CancellationToken ct)
    {
        var erros = new Erros();
        var nome = erros.Nome("nome", pedido.Nome);
        var documento = Cnpj.Normalizar(pedido.Documento ?? "");
        if (!Cnpj.Valido(documento))
            erros.Add("documento", "Informe um CNPJ válido.");
        if (pedido.Gestor is null)
            erros.Add("gestor", "Informe o primeiro gestor da empresa.");
        var (gestorNome, gestorEmail) = erros.Gestor("gestor.", pedido.Gestor ?? new NovoGestor(null, null));

        if (erros.Algum)
            return erros.Resultado();

        if (await db.Tenants.AnyAsync(t => t.Documento == documento, ct))
            return Conflito("Já existe uma empresa com este CNPJ.");
        if (await EmailEmUso(db, gestorEmail, ct))
            return EmailOcupado();

        var empresa = new Tenant { Nome = nome, Documento = documento };
        var gestor = NovoUsuarioGestor(empresa.Id, gestorNome, gestorEmail);
        var (convite, token) = NovoConvite(gestor);

        db.AddRange(empresa, gestor, convite);
        if (!await Gravar(db, ct))
            return Conflito("O CNPJ ou o e-mail do gestor acabou de ser cadastrado por outra pessoa.");

        logs.CreateLogger(nameof(EmpresasEndpoints)).LogInformation(
            "Administrador {AdministradorId} criou a empresa {TenantId} com o gestor {UsuarioId}",
            usuario.AdministradorId(), empresa.Id, gestor.Id);

        return Results.Created($"/empresas/{empresa.Id}", new EmpresaCriada(
            ResumoEmpresa.De(empresa), Resumo(gestor, convite), new ConviteEmitido(opcoes.Link(token), convite.ExpiraEm)));
    }

    /// <summary>Gestor adicional, ou substituto quando o primeiro saiu ou foi cadastrado com e-mail errado.</summary>
    private static async Task<IResult> ConvidarGestor(Guid id, NovoGestor pedido, CampoSeguroDbContext db, OpcoesConvite opcoes,
        ClaimsPrincipal usuario, ILoggerFactory logs, CancellationToken ct)
    {
        var erros = new Erros();
        var (nome, email) = erros.Gestor("", pedido);
        if (erros.Algum)
            return erros.Resultado();

        if (!await db.Tenants.AnyAsync(t => t.Id == id, ct))
            return EmpresaNaoEncontrada();
        if (await EmailEmUso(db, email, ct))
            return EmailOcupado();

        var gestor = NovoUsuarioGestor(id, nome, email);
        var (convite, token) = NovoConvite(gestor);

        db.AddRange(gestor, convite);
        if (!await Gravar(db, ct))
            return EmailOcupado();

        logs.CreateLogger(nameof(EmpresasEndpoints)).LogInformation(
            "Administrador {AdministradorId} convidou o gestor {UsuarioId} na empresa {TenantId}",
            usuario.AdministradorId(), gestor.Id, id);

        return Results.Created($"/empresas/{id}", new GestorConvidado(
            Resumo(gestor, convite), new ConviteEmitido(opcoes.Link(token), convite.ExpiraEm)));
    }

    /// <summary>
    /// Revoga o convite pendente e emite outro. O banco aceita só um pendente por usuário e tipo,
    /// por isso a revogação é gravada antes da inclusão, na mesma transação.
    /// </summary>
    private static async Task<IResult> ReenviarConvite(Guid id, Guid gestorId, CampoSeguroDbContext db, OpcoesConvite opcoes,
        ClaimsPrincipal usuario, ILoggerFactory logs, CancellationToken ct)
    {
        var gestor = await db.DeTodosOsTenants<Usuario>()
            .SingleOrDefaultAsync(u => u.Id == gestorId && u.TenantId == id && u.Papel == Papel.Gestor, ct);

        if (gestor is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Gestor não encontrado nesta empresa.");
        if (!gestor.Ativo)
            return Conflito("O gestor está desativado.");
        if (gestor.SenhaHash is not null)
            return Conflito("O gestor já definiu a senha. Para recuperar o acesso, ele deve usar \"Esqueci minha senha\".");

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var agora = DateTimeOffset.UtcNow;
        foreach (var anterior in await ConvitesPendentes(db, [gestor.Id]).ToListAsync(ct))
            anterior.RevogadoEm = agora;
        await db.SaveChangesAsync(ct);

        var (convite, token) = NovoConvite(gestor);
        db.Convites.Add(convite);
        if (!await Gravar(db, ct))
            return Conflito("Outro convite foi emitido ao mesmo tempo. Consulte a empresa e tente de novo.");

        await transacao.CommitAsync(ct);

        logs.CreateLogger(nameof(EmpresasEndpoints)).LogInformation(
            "Administrador {AdministradorId} reenviou o convite do gestor {UsuarioId} na empresa {TenantId}",
            usuario.AdministradorId(), gestor.Id, id);

        return Results.Ok(new ConviteEmitido(opcoes.Link(token), convite.ExpiraEm));
    }

    private static Usuario NovoUsuarioGestor(Guid tenantId, string nome, string email) => new()
    {
        TenantId = tenantId,
        Nome = nome,
        Email = email,
        EmailNormalizado = Emails.Normalizar(email),
        Papel = Papel.Gestor,
    };

    private static (Convite Convite, string Token) NovoConvite(Usuario gestor)
    {
        var token = Tokens.Gerar();
        return (new Convite
        {
            TenantId = gestor.TenantId,
            UsuarioId = gestor.Id,
            Tipo = TipoConvite.Convite,
            TokenHash = Tokens.Hash(token),
            ExpiraEm = DateTimeOffset.UtcNow + Convite.Validade,
        }, token);
    }

    /// <summary>Convite de primeiro acesso ainda não usado nem revogado; pode estar vencido.</summary>
    private static IQueryable<Convite> ConvitesPendentes(CampoSeguroDbContext db, List<Guid> usuarioIds) =>
        db.DeTodosOsTenants<Convite>().Where(c =>
            usuarioIds.Contains(c.UsuarioId) && c.Tipo == TipoConvite.Convite && c.UsadoEm == null && c.RevogadoEm == null);

    /// <summary>O e-mail é único entre usuários ativos de todas as empresas (ARQUITETURA.md §11).</summary>
    private static Task<bool> EmailEmUso(CampoSeguroDbContext db, string email, CancellationToken ct)
    {
        var normalizado = Emails.Normalizar(email);
        return db.DeTodosOsTenants<Usuario>().AnyAsync(u => u.EmailNormalizado == normalizado && u.Ativo, ct);
    }

    private static ResumoGestor Resumo(Usuario u, Convite? pendente)
    {
        var situacao =
            !u.Ativo ? SituacaoGestor.Desativado
            : u.SenhaHash is not null ? SituacaoGestor.Ativo
            : pendente is not null && pendente.ExpiraEm > DateTimeOffset.UtcNow ? SituacaoGestor.ConvitePendente
            : SituacaoGestor.ConviteExpirado;

        return new ResumoGestor(u.Id, u.Nome, u.Email, situacao, situacao == SituacaoGestor.Ativo ? null : pendente?.ExpiraEm);
    }

    /// <summary>
    /// As consultas de unicidade acima dão a mensagem certa no caso comum; esta é a rede de segurança
    /// para duas requisições simultâneas, quando quem decide é o índice único do banco.
    /// </summary>
    private static async Task<bool> Gravar(CampoSeguroDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    private static IResult EmpresaNaoEncontrada() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Empresa não encontrada.");

    private static IResult EmailOcupado() =>
        Conflito("Este e-mail já pertence a um usuário ativo de uma empresa. Ele precisa ser desativado lá antes.");

    private static IResult Conflito(string titulo) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: titulo);

    private sealed class Erros
    {
        private readonly Dictionary<string, string[]> erros = [];

        public bool Algum => erros.Count > 0;

        public void Add(string campo, string mensagem) => erros[campo] = [mensagem];

        public IResult Resultado() => Results.ValidationProblem(erros, title: "Dados inválidos.");

        public string Nome(string campo, string? valor)
        {
            var limpo = valor?.Trim() ?? "";
            if (limpo.Length == 0)
                Add(campo, "Informe o nome.");
            else if (limpo.Length > Tamanhos.Nome)
                Add(campo, $"O nome pode ter no máximo {Tamanhos.Nome} caracteres.");
            return limpo;
        }

        public (string Nome, string Email) Gestor(string prefixo, NovoGestor gestor)
        {
            var nome = Nome($"{prefixo}nome", gestor.Nome);
            var email = gestor.Email?.Trim() ?? "";
            if (!Emails.Valido(email))
                Add($"{prefixo}email", "Informe um e-mail válido.");
            return (nome, email);
        }
    }
}
