using CampoSeguro.Data;
using CampoSeguro.Data.Configuracoes;
using CampoSeguro.Data.Seguranca;
using CampoSeguro.Domain;
using CampoSeguro.Manager.Api.Convites;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Manager.Api.Colaboradores;

/// <summary>
/// Equipe da empresa: o gestor cadastra técnicos (e outros gestores), gera os links de acesso e
/// desativa quem sai. Tudo passa pelo filtro de tenant: um id de outra empresa simplesmente não existe aqui.
/// Sem serviço de e-mail, o link volta na resposta para o gestor repassar (ARQUITETURA §5).
/// </summary>
public static class ColaboradoresEndpoints
{
    public static void MapColaboradores(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/colaboradores").WithTags("Colaboradores");

        grupo.MapGet("/", Listar);
        grupo.MapGet("/{id:guid}", Detalhar);
        grupo.MapPost("/", Cadastrar);
        grupo.MapPost("/{id:guid}/convite", ReenviarConvite);
        grupo.MapPost("/{id:guid}/redefinicao-senha", RedefinirSenha);
        grupo.MapPost("/{id:guid}/desativar", Desativar);
        grupo.MapPost("/{id:guid}/reativar", Reativar);
        grupo.MapPost("/{id:guid}/anonimizar", Anonimizar);
        grupo.MapGet("/{id:guid}/sessoes", Sessoes);
        grupo.MapPost("/{id:guid}/sessoes/{sessaoId:guid}/revogar", RevogarSessao);
    }

    /// <summary>Aparelhos em que o colaborador entrou no app, os mais usados recentemente primeiro.</summary>
    private static async Task<IResult> Sessoes(Guid id, CampoSeguroDbContext db, CancellationToken ct)
    {
        if (!await db.Usuarios.AnyAsync(u => u.Id == id, ct))
            return NaoEncontrado();

        var agora = DateTimeOffset.UtcNow;
        var sessoes = await db.SessoesDispositivo.AsNoTracking()
            .Where(s => s.UsuarioId == id)
            .OrderByDescending(s => s.UltimoUsoEm ?? s.CriadoEm)
            .ToListAsync(ct);

        return Results.Ok(sessoes.Select(s =>
            new SessaoDoAparelho(s.Id, s.Dispositivo, s.CriadoEm, s.UltimoUsoEm, s.ExpiraEm, s.RevogadaEm is null && s.ExpiraEm > agora)));
    }

    /// <summary>Aparelho perdido ou trocado: corta só aquele aparelho, sem desativar o colaborador.</summary>
    private static async Task<IResult> RevogarSessao(Guid id, Guid sessaoId, CampoSeguroDbContext db, IContextoTenant contexto,
        ILoggerFactory logs, CancellationToken ct)
    {
        var sessao = await db.SessoesDispositivo.SingleOrDefaultAsync(s => s.Id == sessaoId && s.UsuarioId == id, ct);
        if (sessao is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Sessão não encontrada.");

        if (sessao.RevogadaEm is null)
        {
            sessao.RevogadaEm = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logs.CreateLogger(nameof(ColaboradoresEndpoints)).LogInformation(
                "Gestor {GestorId} revogou a sessão {SessaoId} do colaborador {UsuarioId}", contexto.UsuarioId, sessaoId, id);
        }

        return Results.NoContent();
    }

    /// <summary>Por padrão só os ativos; desativados somem do Painel, mas continuam consultáveis.</summary>
    private static async Task<IResult> Listar(bool? incluirDesativados, CampoSeguroDbContext db, CancellationToken ct)
    {
        var consulta = db.Usuarios.AsNoTracking();
        if (incluirDesativados != true)
            consulta = consulta.Where(u => u.Ativo);

        var usuarios = await consulta.OrderBy(u => u.Nome).ToListAsync(ct);
        var convites = await ConvitesPendentes(db).AsNoTracking().ToListAsync(ct);
        return Results.Ok(usuarios.Select(u => Resumo(u, convites.SingleOrDefault(c => c.UsuarioId == u.Id))));
    }

    private static async Task<IResult> Detalhar(Guid id, CampoSeguroDbContext db, CancellationToken ct)
    {
        var usuario = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        return usuario is null ? NaoEncontrado() : Results.Ok(await Resumo(db, usuario, ct));
    }

    private static async Task<IResult> Cadastrar(NovoColaborador pedido, CampoSeguroDbContext db, BuscaSemTenant busca,
        OpcoesConvite opcoes, IContextoTenant contexto, ILoggerFactory logs, CancellationToken ct)
    {
        var erros = new Dictionary<string, string[]>();
        var nome = pedido.Nome?.Trim() ?? "";
        var email = pedido.Email?.Trim() ?? "";
        if (nome.Length == 0)
            erros["nome"] = ["Informe o nome."];
        else if (nome.Length > Tamanhos.Nome)
            erros["nome"] = [$"O nome pode ter no máximo {Tamanhos.Nome} caracteres."];
        if (!Emails.Valido(email))
            erros["email"] = ["Informe um e-mail válido."];
        if (erros.Count > 0)
            return Results.ValidationProblem(erros, title: "Dados inválidos.");

        var normalizado = Emails.Normalizar(email);
        if (await busca.EmailEmUsoPorAtivo(normalizado, ct))
            return EmailOcupado();

        // O tenant vem do contexto (token), não do pedido: o DbContext preenche ao gravar.
        var usuario = new Usuario { Nome = nome, Email = email, EmailNormalizado = normalizado, Papel = pedido.Papel ?? Papel.Tecnico };
        var (convite, token) = LinksDeAcesso.Novo(usuario, TipoConvite.Convite);
        db.Usuarios.Add(usuario);
        db.Convites.Add(convite);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.ViolouUnicidade())
        {
            return EmailOcupado();
        }

        logs.CreateLogger(nameof(ColaboradoresEndpoints)).LogInformation(
            "Gestor {GestorId} cadastrou {Papel} {UsuarioId} na empresa {TenantId}",
            contexto.UsuarioId, usuario.Papel, usuario.Id, usuario.TenantId);

        return Results.Created($"/colaboradores/{usuario.Id}",
            new ColaboradorConvidado(Resumo(usuario, convite), new ConviteEmitido(opcoes.Link(token), convite.ExpiraEm)));
    }

    /// <summary>Para quem ainda não definiu a senha: revoga o link pendente e emite outro.</summary>
    private static async Task<IResult> ReenviarConvite(Guid id, CampoSeguroDbContext db, OpcoesConvite opcoes,
        IContextoTenant contexto, ILoggerFactory logs, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null)
            return NaoEncontrado();
        if (!usuario.Ativo)
            return Conflito("O colaborador está desativado.");
        if (usuario.SenhaHash is not null)
            return Conflito("O colaborador já definiu a senha. Se ele a esqueceu, gere um link de redefinição de senha.");

        return await EmitirLink(usuario, TipoConvite.Convite, db, opcoes, contexto, logs, ct);
    }

    /// <summary>
    /// "Esqueci minha senha" do técnico, enquanto não há e-mail. Só para técnicos: o link chega às
    /// mãos do gestor, e um gestor não deve poder assumir a conta de outro. Gestor recupera a senha
    /// pela Plataforma.
    /// </summary>
    private static async Task<IResult> RedefinirSenha(Guid id, CampoSeguroDbContext db, OpcoesConvite opcoes,
        IContextoTenant contexto, ILoggerFactory logs, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null)
            return NaoEncontrado();
        if (usuario.Papel != Papel.Tecnico)
            return Conflito("Gestores recuperam a senha pela equipe CampoSeguro.");
        if (!usuario.Ativo)
            return Conflito("O colaborador está desativado.");
        if (usuario.SenhaHash is null)
            return Conflito("O colaborador ainda não definiu a senha. Reenvie o convite.");

        return await EmitirLink(usuario, TipoConvite.RedefinicaoSenha, db, opcoes, contexto, logs, ct);
    }

    /// <summary>
    /// Corta o acesso na hora: revoga links pendentes e sessões dos aparelhos. O registro continua,
    /// porque as APRs que o colaborador assinou precisam manter o autor, e o e-mail fica livre para
    /// outra empresa.
    /// </summary>
    private static async Task<IResult> Desativar(Guid id, CampoSeguroDbContext db, IContextoTenant contexto,
        ILoggerFactory logs, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null)
            return NaoEncontrado();
        if (!usuario.Ativo)
            return Results.Ok(await Resumo(db, usuario, ct));
        // Quem desativa é sempre um gestor ativo e não pode desativar a si mesmo, então a empresa
        // nunca fica sem gestor por esta rota.
        if (usuario.Id == contexto.UsuarioId)
            return Conflito("Você não pode desativar o próprio acesso.");

        var agora = DateTimeOffset.UtcNow;
        usuario.Ativo = false;
        usuario.DesativadoEm = agora;
        foreach (var convite in await db.Convites.Where(c => c.UsuarioId == id && c.UsadoEm == null && c.RevogadoEm == null).ToListAsync(ct))
            convite.RevogadoEm = agora;
        foreach (var sessao in await db.SessoesDispositivo.Where(s => s.UsuarioId == id && s.RevogadaEm == null).ToListAsync(ct))
            sessao.RevogadaEm = agora;
        await db.SaveChangesAsync(ct);

        logs.CreateLogger(nameof(ColaboradoresEndpoints)).LogInformation(
            "Gestor {GestorId} desativou o colaborador {UsuarioId}", contexto.UsuarioId, id);

        return Results.Ok(await Resumo(db, usuario, ct));
    }

    /// <summary>
    /// Desfaz uma desativação por engano. A senha antiga volta a valer; os aparelhos, que tiveram a
    /// sessão revogada, precisam entrar de novo.
    /// </summary>
    private static async Task<IResult> Reativar(Guid id, CampoSeguroDbContext db, BuscaSemTenant busca, IContextoTenant contexto,
        ILoggerFactory logs, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null)
            return NaoEncontrado();
        if (usuario.Ativo)
            return Results.Ok(await Resumo(db, usuario, ct));
        if (usuario.AnonimizadoEm is not null)
            return Conflito("Colaborador anonimizado não pode ser reativado. Cadastre de novo.");
        if (await busca.EmailEmUsoPorAtivo(usuario.EmailNormalizado, ct))
            return EmailOcupado();

        usuario.Ativo = true;
        usuario.DesativadoEm = null;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.ViolouUnicidade())
        {
            return EmailOcupado();
        }

        logs.CreateLogger(nameof(ColaboradoresEndpoints)).LogInformation(
            "Gestor {GestorId} reativou o colaborador {UsuarioId}", contexto.UsuarioId, id);

        return Results.Ok(await Resumo(db, usuario, ct));
    }

    /// <summary>LGPD: apaga nome e e-mail de um colaborador já desativado. Irreversível; as APRs mantêm o nome congelado.</summary>
    private static async Task<IResult> Anonimizar(Guid id, CampoSeguroDbContext db, IContextoTenant contexto,
        ILoggerFactory logs, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null)
            return NaoEncontrado();
        if (usuario.Ativo)
            return Conflito("Desative o colaborador antes de anonimizar.");
        if (usuario.AnonimizadoEm is null)
        {
            usuario.Anonimizar(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);

            logs.CreateLogger(nameof(ColaboradoresEndpoints)).LogInformation(
                "Gestor {GestorId} anonimizou o colaborador {UsuarioId}", contexto.UsuarioId, id);
        }

        return Results.Ok(await Resumo(db, usuario, ct));
    }

    private static async Task<IResult> EmitirLink(Usuario usuario, TipoConvite tipo, CampoSeguroDbContext db,
        OpcoesConvite opcoes, IContextoTenant contexto, ILoggerFactory logs, CancellationToken ct)
    {
        Convite convite;
        string token;
        try
        {
            (convite, token) = await LinksDeAcesso.Reemitir(db, db.Convites.Where(c => c.UsuarioId == usuario.Id), usuario, tipo, ct);
        }
        catch (DbUpdateException e) when (e.ViolouUnicidade())
        {
            return Conflito("Outro link foi emitido ao mesmo tempo. Consulte o colaborador e tente de novo.");
        }

        logs.CreateLogger(nameof(ColaboradoresEndpoints)).LogInformation(
            "Gestor {GestorId} emitiu link de {Tipo} para o colaborador {UsuarioId}", contexto.UsuarioId, tipo, usuario.Id);

        return Results.Ok(new ConviteEmitido(opcoes.Link(token), convite.ExpiraEm));
    }

    /// <summary>Convite de primeiro acesso ainda não usado nem revogado; pode estar vencido.</summary>
    private static IQueryable<Convite> ConvitesPendentes(CampoSeguroDbContext db) =>
        db.Convites.Where(c => c.Tipo == TipoConvite.Convite && c.UsadoEm == null && c.RevogadoEm == null);

    private static async Task<Colaborador> Resumo(CampoSeguroDbContext db, Usuario usuario, CancellationToken ct) =>
        Resumo(usuario, await ConvitesPendentes(db).AsNoTracking().SingleOrDefaultAsync(c => c.UsuarioId == usuario.Id, ct));

    private static Colaborador Resumo(Usuario u, Convite? pendente)
    {
        var situacao = u.Situacao(pendente, DateTimeOffset.UtcNow);
        return new Colaborador(u.Id, u.Nome, u.Email, u.Papel, situacao,
            situacao is SituacaoAcesso.ConvitePendente or SituacaoAcesso.ConviteExpirado ? pendente?.ExpiraEm : null,
            u.CriadoEm, u.DesativadoEm, u.AnonimizadoEm is not null);
    }

    private static IResult NaoEncontrado() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Colaborador não encontrado.");

    private static IResult EmailOcupado() =>
        Conflito("Este e-mail já está em uso por um usuário ativo. Se a pessoa saiu de outra empresa, ela precisa ser desativada lá antes.");

    private static IResult Conflito(string titulo) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: titulo);
}
