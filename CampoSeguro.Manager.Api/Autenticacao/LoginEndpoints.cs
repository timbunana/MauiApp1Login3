using CampoSeguro.Data;
using CampoSeguro.Data.Seguranca;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Manager.Api.Autenticacao;

public static class LoginEndpoints
{
    public const string PoliticaLimite = "anonimo";

    public sealed record PedidoLogin(string? Email, string? Senha);

    public sealed record RespostaLogin(string Token, DateTimeOffset ExpiraEm);

    public sealed record PedidoTrocaSenha(string? SenhaAtual, string? SenhaNova);

    public static void MapLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/autenticacao/login", Entrar)
            .AllowAnonymous()
            .RequireRateLimiting(PoliticaLimite)
            .WithTags("Autenticação");

        app.MapPost("/autenticacao/senha", TrocarSenha)
            .RequireRateLimiting(PoliticaLimite)
            .WithTags("Autenticação");
    }

    /// <summary>
    /// O gestor logado troca a própria senha. Senha atual errada volta 400 (e não 401), para o
    /// frontend não confundir com sessão expirada e deslogar.
    /// </summary>
    private static async Task<IResult> TrocarSenha(PedidoTrocaSenha pedido, CampoSeguroDbContext db, IContextoTenant contexto, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == contexto.UsuarioId, ct);
        if (Senhas.Conferir(usuario.SenhaHash, pedido.SenhaAtual ?? "") == ResultadoSenha.Invalida)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["senhaAtual"] = ["Senha atual incorreta."] }, title: "Dados inválidos.");
        if (Senhas.Problema(pedido.SenhaNova ?? "") is { } problema)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["senhaNova"] = [problema] }, title: "Dados inválidos.");

        usuario.SenhaHash = Senhas.Gerar(pedido.SenhaNova!);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// E-mail inexistente, usuário desativado, convite ainda não aceito e senha errada dão a mesma
    /// resposta, no mesmo tempo. Só depois da senha certa o técnico fica sabendo que o acesso dele é pelo app.
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
            logs.CreateLogger(nameof(LoginEndpoints)).LogWarning("Login recusado no Painel para {Email}", email);
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "E-mail ou senha incorretos.");
        }

        if (usuario.Papel != Papel.Gestor)
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Seu acesso é pelo aplicativo CampoSeguro.");

        if (resultado == ResultadoSenha.PrecisaRegravar)
        {
            usuario.SenhaHash = Senhas.Gerar(senha);
            await db.SaveChangesAsync(ct);
        }

        var (token, expiraEm) = emissor.Emitir(usuario);
        return Results.Ok(new RespostaLogin(token, expiraEm));
    }
}
