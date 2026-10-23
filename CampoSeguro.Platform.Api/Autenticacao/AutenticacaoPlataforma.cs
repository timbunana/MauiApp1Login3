using System.Security.Claims;
using CampoSeguro.Data;
using CampoSeguro.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CampoSeguro.Platform.Api.Autenticacao;

public static class AutenticacaoPlataforma
{
    /// <summary>
    /// JWT assinado com HS256 e, por padrão, <b>toda rota exige autenticação</b>: uma rota nova
    /// nasce protegida e só fica aberta com <c>AllowAnonymous</c> explícito.
    /// </summary>
    public static IServiceCollection AddAutenticacaoPlataforma(this IServiceCollection services, OpcoesJwt jwt)
    {
        services.AddSingleton(jwt);
        services.AddSingleton<EmissorToken>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = jwt.Emissor,
                ValidAudience = jwt.Audiencia,
                IssuerSigningKey = jwt.ChaveAssinatura(),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = JwtRegisteredClaimNames.Name,
            };
            o.Events = new JwtBearerEvents { OnTokenValidated = ConferirAdministradorAtivo };
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    public static Guid AdministradorId(this ClaimsPrincipal usuario) =>
        Guid.Parse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    /// <summary>
    /// Um token válido por assinatura não basta: o administrador pode ter sido desativado depois de
    /// recebê-lo. A Plataforma enxerga todas as empresas, então o custo de uma consulta por requisição
    /// vale o corte imediato do acesso.
    /// </summary>
    private static async Task ConferirAdministradorAtivo(TokenValidatedContext ctx)
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<CampoSeguroDbContext>();
        var ativo = Guid.TryParse(ctx.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            && await db.AdministradoresPlataforma.AnyAsync(a => a.Id == id && a.Ativo, ctx.HttpContext.RequestAborted);

        if (!ativo)
            ctx.Fail("Administrador inexistente ou desativado.");
    }
}

public sealed class EmissorToken(OpcoesJwt opcoes)
{
    private readonly JsonWebTokenHandler handler = new();

    public (string Token, DateTimeOffset ExpiraEm) Emitir(AdministradorPlataforma admin)
    {
        var agora = DateTimeOffset.UtcNow;
        var expiraEm = agora + opcoes.Validade;

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = opcoes.Emissor,
            Audience = opcoes.Audiencia,
            IssuedAt = agora.UtcDateTime,
            NotBefore = agora.UtcDateTime,
            Expires = expiraEm.UtcDateTime,
            SigningCredentials = new SigningCredentials(opcoes.ChaveAssinatura(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = admin.Id.ToString(),
                [JwtRegisteredClaimNames.Name] = admin.Nome,
                [JwtRegisteredClaimNames.Email] = admin.EmailNormalizado,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        });

        return (token, expiraEm);
    }
}
