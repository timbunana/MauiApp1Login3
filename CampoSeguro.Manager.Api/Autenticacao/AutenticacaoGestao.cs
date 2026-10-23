using System.Security.Claims;
using CampoSeguro.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CampoSeguro.Manager.Api.Autenticacao;

public static class AutenticacaoGestao
{
    /// <summary>
    /// JWT HS256. Por padrão <b>toda rota exige um gestor autenticado</b>; rota aberta só com
    /// <c>AllowAnonymous</c> explícito (login e aceite de convite).
    /// </summary>
    public static IServiceCollection AddAutenticacaoGestao(this IServiceCollection services, OpcoesJwt jwt)
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
                RoleClaimType = Claims.Papel,
            };
            o.Events = new JwtBearerEvents { OnTokenValidated = ConferirGestorAtivo };
        });

        services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(nameof(Papel.Gestor))
            .Build());

        return services;
    }

    /// <summary>
    /// A assinatura prova que o token foi emitido aqui, não que o gestor ainda está ativo. Sem esta
    /// consulta, desativar um gestor deixaria o token valendo até expirar.
    /// </summary>
    private static async Task ConferirGestorAtivo(TokenValidatedContext ctx)
    {
        var busca = ctx.HttpContext.RequestServices.GetRequiredService<BuscaSemTenant>();
        var valido = Guid.TryParse(ctx.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuario)
            && Guid.TryParse(ctx.Principal?.FindFirstValue(Claims.Tenant), out var tenant)
            && await busca.GestorAtivo(usuario, tenant, ctx.HttpContext.RequestAborted);

        if (!valido)
            ctx.Fail("Gestor inexistente, desativado ou de outra empresa.");
    }
}

public sealed class EmissorToken(OpcoesJwt opcoes)
{
    private readonly JsonWebTokenHandler handler = new();

    public (string Token, DateTimeOffset ExpiraEm) Emitir(Usuario usuario)
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
                [JwtRegisteredClaimNames.Sub] = usuario.Id.ToString(),
                [Claims.Tenant] = usuario.TenantId.ToString(),
                [Claims.Papel] = usuario.Papel.ToString(),
                [JwtRegisteredClaimNames.Name] = usuario.Nome,
                [JwtRegisteredClaimNames.Email] = usuario.EmailNormalizado,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        });

        return (token, expiraEm);
    }
}
