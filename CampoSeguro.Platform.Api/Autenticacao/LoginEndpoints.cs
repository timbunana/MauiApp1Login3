using CampoSeguro.Data;
using CampoSeguro.Data.Seguranca;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Platform.Api.Autenticacao;

public static class LoginEndpoints
{
    public const string PoliticaLimite = "login";

    public sealed record PedidoLogin(string? Email, string? Senha);

    public sealed record RespostaLogin(string Token, DateTimeOffset ExpiraEm);

    public static void MapLogin(this IEndpointRouteBuilder app) =>
        app.MapPost("/autenticacao/login", Entrar)
            .AllowAnonymous()
            .RequireRateLimiting(PoliticaLimite)
            .WithTags("Autenticação");

    /// <summary>
    /// E-mail inexistente, administrador desativado e senha errada dão a mesma resposta, no mesmo
    /// tempo (<see cref="Senhas.Conferir"/> roda mesmo sem hash): a tela de login não revela quem existe.
    /// </summary>
    private static async Task<IResult> Entrar(PedidoLogin pedido, CampoSeguroDbContext db, EmissorToken emissor,
        ILoggerFactory logs, CancellationToken ct)
    {
        var email = Emails.Normalizar(pedido.Email ?? "");
        var senha = pedido.Senha ?? "";

        var admin = await db.AdministradoresPlataforma.SingleOrDefaultAsync(a => a.EmailNormalizado == email && a.Ativo, ct);
        var resultado = Senhas.Conferir(admin?.SenhaHash, senha);

        if (admin is null || resultado == ResultadoSenha.Invalida)
        {
            logs.CreateLogger(nameof(LoginEndpoints)).LogWarning("Login recusado na Plataforma para {Email}", email);
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "E-mail ou senha incorretos.");
        }

        if (resultado == ResultadoSenha.PrecisaRegravar)
        {
            admin.SenhaHash = Senhas.Gerar(senha);
            await db.SaveChangesAsync(ct);
        }

        var (token, expiraEm) = emissor.Emitir(admin);
        return Results.Ok(new RespostaLogin(token, expiraEm));
    }
}
