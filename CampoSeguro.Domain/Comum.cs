namespace CampoSeguro.Domain;

/// <summary>Registro com trilha mínima de auditoria, preenchida pelo DbContext ao inserir.</summary>
public interface IAuditavel
{
    DateTimeOffset CriadoEm { get; set; }

    /// <summary>Usuário que criou o registro. Nulo quando criado pelo sistema ou pela Plataforma.</summary>
    Guid? CriadoPor { get; set; }
}

/// <summary>Registro que pertence a uma empresa-cliente e é isolado pelo filtro global de tenant.</summary>
public interface IPertenceAoTenant
{
    Guid TenantId { get; set; }
}

public abstract class Entidade : IAuditavel
{
    /// <summary>GUID v7 por padrão: ordenável no tempo, o que mantém os índices compactos.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CriadoEm { get; set; }
    public Guid? CriadoPor { get; set; }
}

public abstract class EntidadeDoTenant : Entidade, IPertenceAoTenant
{
    public Guid TenantId { get; set; }
}
