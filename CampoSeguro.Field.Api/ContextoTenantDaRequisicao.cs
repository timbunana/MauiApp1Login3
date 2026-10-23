using System.Security.Claims;
using CampoSeguro.Data;
using CampoSeguro.Field.Api.Autenticacao;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CampoSeguro.Field.Api;

/// <summary>
/// Tenant, usuário e sessão lidos <b>só do token</b>, nunca do corpo ou da query. O filtro do EF lê
/// <see cref="TenantId"/> a cada consulta; sem token, o tenant é nulo e nada é visível.
/// </summary>
public sealed class ContextoTenantDaRequisicao(IHttpContextAccessor http) : IContextoTenant
{
    public Guid? TenantId => Ler(Claims.Tenant);

    public Guid? UsuarioId => Ler(JwtRegisteredClaimNames.Sub);

    public Guid? SessaoId => Ler(Claims.Sessao);

    private Guid? Ler(string claim) =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(claim), out var valor) ? valor : null;
}
