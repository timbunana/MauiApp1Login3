namespace CampoSeguro.Data;

/// <summary>
/// Quem está fazendo a requisição. Cada backend implementa a partir do JWT —
/// o TenantId nunca vem do corpo ou da query da requisição.
/// </summary>
public interface IContextoTenant
{
    /// <summary>
    /// Tenant do token. Nulo significa "nenhum tenant": o filtro global não devolve nenhuma linha
    /// de negócio. Só a Plataforma, o Migrator e a busca de login operam assim.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>Usuário autenticado, gravado em CriadoPor. Nulo para operações do sistema.</summary>
    Guid? UsuarioId { get; }
}

public sealed record ContextoTenantFixo(Guid? TenantId, Guid? UsuarioId = null) : IContextoTenant
{
    public static readonly ContextoTenantFixo SemTenant = new(TenantId: null);
}
