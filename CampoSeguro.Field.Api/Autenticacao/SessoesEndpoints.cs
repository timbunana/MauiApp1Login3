using CampoSeguro.Data;
using CampoSeguro.Data.Seguranca;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Field.Api.Autenticacao;

/// <summary>
/// Sessão por aparelho. O técnico digita a senha uma vez; depois o app guarda o token de renovação no
/// SecureStorage e troca por tokens de acesso curtos ao sincronizar.
/// </summary>
public static class SessoesEndpoints
{
    public const string PoliticaLimite = "anonimo";

    public sealed record PedidoLogin(string? Email, string? Senha, string? Dispositivo);

    public sealed record PedidoRenovacao(string? TokenRenovacao);

    public sealed record Empresa(Guid Id, string Nome);

    public sealed record Tecnico(Guid Id, string Nome, string Email);

    /// <summary>
    /// <see cref="Tecnico"/>.Id é o autor que o app usa no hash da APR (<see cref="ConteudoApr"/>).
    /// O token de renovação só aparece no login; a renovação devolve o mesmo.
    /// </summary>
    public sealed record Sessao(
        string TokenAcesso, DateTimeOffset AcessoExpiraEm, string TokenRenovacao, DateTimeOffset RenovacaoExpiraEm,
        Tecnico Tecnico, Empresa Empresa);

    public static void MapSessoes(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/autenticacao").WithTags("Autenticação");

        grupo.MapPost("/login", Entrar).AllowAnonymous().RequireRateLimiting(PoliticaLimite);
        grupo.MapPost("/renovar", Renovar).AllowAnonymous().RequireRateLimiting(PoliticaLimite);
        grupo.MapPost("/sair", Sair);
        grupo.MapPost("/senha", TrocarSenha).RequireRateLimiting(PoliticaLimite);
    }

    public sealed record PedidoTrocaSenha(string? SenhaAtual, string? SenhaNova);

    /// <summary>
    /// O técnico troca a própria senha pelo app. Os outros aparelhos dele saem (quem trocou a senha por
    /// desconfiança espera isso); este aparelho continua conectado. Senha atual errada volta 400, não
    /// 401, para o app não tratar como sessão encerrada.
    /// </summary>
    private static async Task<IResult> TrocarSenha(PedidoTrocaSenha pedido, CampoSeguroDbContext db,
        ContextoTenantDaRequisicao contexto, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == contexto.UsuarioId, ct);
        if (Senhas.Conferir(usuario.SenhaHash, pedido.SenhaAtual ?? "") == ResultadoSenha.Invalida)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["senhaAtual"] = ["Senha atual incorreta."] }, title: "Dados inválidos.");
        if (Senhas.Problema(pedido.SenhaNova ?? "") is { } problema)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["senhaNova"] = [problema] }, title: "Dados inválidos.");

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        usuario.SenhaHash = Senhas.Gerar(pedido.SenhaNova!);
        await db.SaveChangesAsync(ct);
        await db.SessoesDispositivo
            .Where(s => s.UsuarioId == usuario.Id && s.Id != contexto.SessaoId && s.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, DateTimeOffset.UtcNow), ct);
        await transacao.CommitAsync(ct);

        return Results.NoContent();
    }

    /// <summary>
    /// Mesma resposta para e-mail inexistente, desativado, convite não aceito e senha errada. Gestor
    /// com a senha certa é avisado de que o acesso dele é pelo Painel.
    /// </summary>
    private static async Task<IResult> Entrar(PedidoLogin pedido, BuscaSemTenant busca, CampoSeguroDbContext db,
        EmissorToken emissor, ILoggerFactory logs, CancellationToken ct)
    {
        var email = Emails.Normalizar(pedido.Email ?? "");
        var senha = pedido.Senha ?? "";

        var usuario = await busca.UsuarioAtivoPorEmail(email, ct);
        var resultado = Senhas.Conferir(usuario?.SenhaHash, senha);

        if (usuario is null || resultado == ResultadoSenha.Invalida)
        {
            logs.CreateLogger(nameof(SessoesEndpoints)).LogWarning("Login recusado no app para {Email}", email);
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "E-mail ou senha incorretos.");
        }

        if (usuario.Papel != Papel.Tecnico)
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Seu acesso é pelo Painel de gestão.");

        if (resultado == ResultadoSenha.PrecisaRegravar)
            usuario.SenhaHash = Senhas.Gerar(senha);

        var tokenRenovacao = Tokens.Gerar();
        var sessao = new SessaoDispositivo
        {
            TenantId = usuario.TenantId,
            UsuarioId = usuario.Id,
            TokenHash = Tokens.Hash(tokenRenovacao),
            Dispositivo = Limitar(pedido.Dispositivo?.Trim(), 200),
            ExpiraEm = DateTimeOffset.UtcNow + SessaoDispositivo.Validade,
            UltimoUsoEm = DateTimeOffset.UtcNow,
        };
        db.SessoesDispositivo.Add(sessao);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Resposta(usuario, sessao, tokenRenovacao, emissor, busca, ct));
    }

    /// <summary>
    /// Troca o token de renovação por um token de acesso novo e estende a sessão por mais 90 dias.
    /// <b>Não rotaciona</b> o token de renovação: se a resposta se perder numa rede ruim, o app tenta
    /// de novo com o mesmo token. Com rotação, ele ficaria com um token já invalidado e o técnico seria
    /// deslogado em campo. A proteção contra roubo é a revogação (desativar, redefinir senha, sair).
    /// </summary>
    private static async Task<IResult> Renovar(PedidoRenovacao pedido, BuscaSemTenant busca, CampoSeguroDbContext db,
        EmissorToken emissor, CancellationToken ct)
    {
        var token = pedido.TokenRenovacao ?? "";
        var agora = DateTimeOffset.UtcNow;
        var sessao = token.Length is > 0 and <= 100 ? await busca.SessaoPorToken(Tokens.Hash(token), ct) : null;

        if (sessao is not { RevogadaEm: null, Usuario: { Ativo: true, Papel: Papel.Tecnico } } || sessao.ExpiraEm <= agora)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sessão encerrada. Entre de novo com e-mail e senha.");

        sessao.UltimoUsoEm = agora;
        sessao.ExpiraEm = agora + SessaoDispositivo.Validade;
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Resposta(sessao.Usuario, sessao, token, emissor, busca, ct));
    }

    /// <summary>Encerra a sessão deste aparelho (troca de aparelho, aparelho compartilhado).</summary>
    private static async Task<IResult> Sair(CampoSeguroDbContext db, ContextoTenantDaRequisicao contexto, CancellationToken ct)
    {
        await db.SessoesDispositivo.Where(s => s.Id == contexto.SessaoId && s.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, DateTimeOffset.UtcNow), ct);
        return Results.NoContent();
    }

    private static async Task<Sessao> Resposta(Usuario usuario, SessaoDispositivo sessao, string tokenRenovacao,
        EmissorToken emissor, BuscaSemTenant busca, CancellationToken ct)
    {
        var (acesso, acessoExpiraEm) = emissor.Emitir(usuario, sessao);
        return new Sessao(acesso, acessoExpiraEm, tokenRenovacao, sessao.ExpiraEm,
            new Tecnico(usuario.Id, usuario.Nome, usuario.Email),
            new Empresa(usuario.TenantId, await busca.NomeDaEmpresa(usuario.TenantId, ct)));
    }

    private static string? Limitar(string? texto, int maximo) =>
        string.IsNullOrEmpty(texto) ? null : texto.Length <= maximo ? texto : texto[..maximo];
}
