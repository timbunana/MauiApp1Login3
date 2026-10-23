using System.Security.Claims;
using CampoSeguro.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CampoSeguro.Field.Api.Autenticacao;

public static class AutenticacaoCampo
{
    /// <summary>
    /// JWT HS256. Por padrão <b>toda rota exige um técnico com sessão ativa</b>; só login e renovação
    /// têm <c>AllowAnonymous</c>.
    /// </summary>
    public static IServiceCollection AddAutenticacaoCampo(this IServiceCollection services, OpcoesJwt jwt)
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
            o.Events = new JwtBearerEvents { OnTokenValidated = ConferirSessao };
        });

        services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(nameof(Papel.Tecnico))
            .Build());

        return services;
    }

    private static async Task ConferirSessao(TokenValidatedContext ctx)
    {
        var busca = ctx.HttpContext.RequestServices.GetRequiredService<BuscaSemTenant>();
        var principal = ctx.Principal;
        var valida = Guid.TryParse(principal?.FindFirstValue(Claims.Sessao), out var sessao)
            && Guid.TryParse(principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuario)
            && Guid.TryParse(principal?.FindFirstValue(Claims.Tenant), out var tenant)
            && await busca.SessaoAtiva(sessao, usuario, tenant, DateTimeOffset.UtcNow, ctx.HttpContext.RequestAborted);

        if (!valida)
            ctx.Fail("Sessão revogada, expirada ou de técnico desativado.");
    }
}

public sealed class EmissorToken(OpcoesJwt opcoes)
{
    private readonly JsonWebTokenHandler handler = new();

    public (string Token, DateTimeOffset ExpiraEm) Emitir(Usuario usuario, SessaoDispositivo sessao)
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
                [Claims.Sessao] = sessao.Id.ToString(),
                [Claims.Papel] = usuario.Papel.ToString(),
                [JwtRegisteredClaimNames.Name] = usuario.Nome,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        });

        return (token, expiraEm);
    }
}
