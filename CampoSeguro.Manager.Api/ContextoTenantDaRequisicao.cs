using System.Security.Claims;
using CampoSeguro.Data;
using CampoSeguro.Manager.Api.Autenticacao;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CampoSeguro.Manager.Api;

/// <summary>
/// Tenant e usuário lidos <b>só do token</b>, nunca do corpo ou da query. Lido a cada consulta
/// (o filtro do EF consulta <see cref="TenantId"/> na hora), então funciona mesmo se o DbContext
/// foi criado antes da autenticação terminar. Sem token, o tenant é nulo e nada é visível.
/// </summary>
public sealed class ContextoTenantDaRequisicao(IHttpContextAccessor http) : IContextoTenant
{
    public Guid? TenantId => Ler(Claims.Tenant);

    public Guid? UsuarioId => Ler(JwtRegisteredClaimNames.Sub);

    private Guid? Ler(string claim) =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(claim), out var valor) ? valor : null;
}
