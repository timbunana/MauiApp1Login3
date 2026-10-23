using CampoSeguro.Data;
using CampoSeguro.Data.Seguranca;
using CampoSeguro.Domain;
using CampoSeguro.Manager.Api.Autenticacao;

namespace CampoSeguro.Manager.Api.Convites;

/// <summary>
/// Página de aceite do link de convite (ou de redefinição de senha). Serve a qualquer colaborador:
/// o gestor entra depois no Painel, o técnico no app. O token chega no corpo de um POST — a página
/// o lê do fragmento do link (<c>#token</c>), que nunca vai para o servidor nem para os logs.
/// </summary>
public static class ConvitesEndpoints
{
    public sealed record PedidoToken(string? Token);

    public sealed record PedidoAceite(string? Token, string? Senha);

    public sealed record DadosConvite(string Nome, string Email, string Empresa, TipoConvite Tipo, DateTimeOffset ExpiraEm);

    public static void MapConvites(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/convites").AllowAnonymous().RequireRateLimiting(LoginEndpoints.PoliticaLimite).WithTags("Convites");

        grupo.MapPost("/consultar", Consultar);
        grupo.MapPost("/aceitar", Aceitar);
    }

    /// <summary>Para a página mostrar "Olá, Gil" antes de pedir a senha, e já avisar se o link não vale.</summary>
    private static async Task<IResult> Consultar(PedidoToken pedido, BuscaSemTenant busca, CancellationToken ct)
    {
        var convite = await Valido(pedido.Token, busca, ct);
        if (convite is null)
            return LinkInvalido();

        var empresa = await busca.NomeDaEmpresa(convite.TenantId, ct);
        return Results.Ok(new DadosConvite(convite.Usuario!.Nome, convite.Usuario.Email, empresa, convite.Tipo, convite.ExpiraEm));
    }

    private static async Task<IResult> Aceitar(PedidoAceite pedido, BuscaSemTenant busca, CampoSeguroDbContext db,
        ILoggerFactory logs, CancellationToken ct)
    {
        var senha = pedido.Senha ?? "";
        if (Senhas.Problema(senha) is { } problema)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["senha"] = [problema] }, title: "Dados inválidos.");

        var convite = await Valido(pedido.Token, busca, ct);
        if (convite is null)
            return LinkInvalido();

        var agora = DateTimeOffset.UtcNow;
        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        if (!await busca.MarcarConviteUsado(convite.Id, agora, ct))
            return LinkInvalido();

        var usuario = convite.Usuario!;
        usuario.SenhaHash = Senhas.Gerar(senha);
        await db.SaveChangesAsync(ct);

        if (convite.Tipo == TipoConvite.RedefinicaoSenha)
            await busca.RevogarSessoes(usuario.Id, usuario.TenantId, agora, ct);

        await transacao.CommitAsync(ct);

        logs.CreateLogger(nameof(ConvitesEndpoints)).LogInformation(
            "Usuário {UsuarioId} da empresa {TenantId} aceitou o convite {ConviteId} ({Tipo})",
            usuario.Id, usuario.TenantId, convite.Id, convite.Tipo);

        return Results.NoContent();
    }

    /// <summary>Não usado, não revogado, dentro do prazo e de um colaborador ainda ativo.</summary>
    private static async Task<Convite?> Valido(string? token, BuscaSemTenant busca, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
            return null;

        var convite = await busca.ConvitePorToken(Tokens.Hash(token), ct);
        return convite is { UsadoEm: null, RevogadoEm: null, Usuario.Ativo: true } && convite.ExpiraEm > DateTimeOffset.UtcNow
            ? convite
            : null;
    }

    /// <summary>
    /// Uma resposta só para inexistente, vencido, usado e revogado: quem tem o link não precisa saber
    /// qual foi o caso para resolver, e quem chuta tokens não aprende nada.
    /// </summary>
    private static IResult LinkInvalido() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Este link é inválido, expirou ou já foi usado. Peça um novo a quem enviou.");
}
