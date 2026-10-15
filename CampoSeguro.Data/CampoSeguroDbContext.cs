using System.Text.RegularExpressions;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CampoSeguro.Data;

public class CampoSeguroDbContext(DbContextOptions<CampoSeguroDbContext> options, IContextoTenant contexto)
    : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AdministradorPlataforma> AdministradoresPlataforma => Set<AdministradorPlataforma>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Convite> Convites => Set<Convite>();
    public DbSet<SessaoDispositivo> SessoesDispositivo => Set<SessaoDispositivo>();
    public DbSet<Catalogo> Catalogos => Set<Catalogo>();
    public DbSet<CatalogoItem> CatalogoItens => Set<CatalogoItem>();
    public DbSet<CatalogoVersao> CatalogoVersoes => Set<CatalogoVersao>();
    public DbSet<CatalogoVersaoItem> CatalogoVersaoItens => Set<CatalogoVersaoItem>();
    public DbSet<Apr> Aprs => Set<Apr>();
    public DbSet<AprItem> AprItens => Set<AprItem>();
    public DbSet<AprEvidencia> AprEvidencias => Set<AprEvidencia>();

    /// <summary>
    /// Lido pelo filtro global a cada consulta (o EF parametriza membros do DbContext).
    /// Precisa ser membro da instância: um valor capturado no OnModelCreating ficaria
    /// congelado no modelo, que é cacheado e compartilhado entre todas as requisições.
    /// </summary>
    private Guid? TenantAtual => contexto.TenantId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(30);

        // O timestamptz do Npgsql só aceita DateTimeOffset com offset zero; o aparelho manda
        // horário local (-03:00). Normalizar aqui evita a exceção em cada ponto de gravação.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<ParaUtc>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CampoSeguroDbContext).Assembly);

        FiltrarPorTenant<Usuario>(modelBuilder);
        FiltrarPorTenant<Convite>(modelBuilder);
        FiltrarPorTenant<SessaoDispositivo>(modelBuilder);
        FiltrarPorTenant<Catalogo>(modelBuilder);
        FiltrarPorTenant<CatalogoItem>(modelBuilder);
        FiltrarPorTenant<CatalogoVersao>(modelBuilder);
        FiltrarPorTenant<CatalogoVersaoItem>(modelBuilder);
        FiltrarPorTenant<Apr>(modelBuilder);
        FiltrarPorTenant<AprItem>(modelBuilder);
        FiltrarPorTenant<AprEvidencia>(modelBuilder);

        GarantirFiltroEmTodoTenant(modelBuilder);
        NomearTabelasEChaves(modelBuilder);
    }

    /// <summary>
    /// Tabela no singular, com o nome da entidade (apr, catalogo_versao) — sem isto o EF usaria o
    /// nome do DbSet. Chave estrangeira como fk_{tabela}_{relação} (fk_apr_autor, fk_apr_tenant):
    /// os nomes gerados misturavam plural e singular e estouravam os 63 caracteres do PostgreSQL.
    /// </summary>
    private static void NomearTabelasEChaves(ModelBuilder modelBuilder)
    {
        foreach (var tipo in modelBuilder.Model.GetEntityTypes())
            tipo.SetTableName(SnakeCase(tipo.ClrType.Name));

        foreach (var tipo in modelBuilder.Model.GetEntityTypes())
        foreach (var chave in tipo.GetForeignKeys())
        {
            var coluna = chave.Properties.Select(p => p.Name).LastOrDefault(n => n != nameof(IPertenceAoTenant.TenantId))
                ?? nameof(IPertenceAoTenant.TenantId);
            chave.SetConstraintName($"fk_{tipo.GetTableName()}_{SnakeCase(coluna[..^"Id".Length])}");
        }
    }

    private static string SnakeCase(string nome) =>
        Regex.Replace(nome, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();

    private void FiltrarPorTenant<T>(ModelBuilder modelBuilder) where T : class, IPertenceAoTenant
    {
        var entidade = modelBuilder.Entity<T>();
        entidade.HasQueryFilter(e => e.TenantId == TenantAtual);
        entidade.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>Uma entidade nova de tenant sem filtro vazaria dados entre empresas: falha já ao montar o modelo.</summary>
    private static void GarantirFiltroEmTodoTenant(ModelBuilder modelBuilder)
    {
        var semFiltro = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(IPertenceAoTenant).IsAssignableFrom(t.ClrType) && t.GetDeclaredQueryFilters().Count == 0)
            .Select(t => t.ClrType.Name)
            .ToList();

        if (semFiltro.Count > 0)
            throw new InvalidOperationException($"Entidades de tenant sem filtro global: {string.Join(", ", semFiltro)}.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepararGravacao();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepararGravacao();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Preenche auditoria e impede gravar no tenant errado. Com um tenant no contexto, todo registro
    /// novo herda esse tenant e qualquer outro é recusado. Sem tenant (Plataforma, Migrator), o
    /// TenantId precisa vir explícito — é assim que a Plataforma cria o primeiro gestor de uma empresa.
    /// </summary>
    private void PrepararGravacao()
    {
        var agora = DateTimeOffset.UtcNow;

        foreach (var entrada in ChangeTracker.Entries())
        {
            if (entrada.State == EntityState.Added && entrada.Entity is IAuditavel auditavel)
            {
                if (auditavel.CriadoEm == default)
                    auditavel.CriadoEm = agora;
                auditavel.CriadoPor ??= contexto.UsuarioId;
            }

            if (entrada.Entity is not IPertenceAoTenant registro)
                continue;

            if (entrada.State == EntityState.Added)
            {
                if (registro.TenantId == Guid.Empty)
                    registro.TenantId = contexto.TenantId
                        ?? throw new InvalidOperationException($"{entrada.Metadata.ClrType.Name} sem TenantId e sem tenant no contexto.");
                else if (contexto.TenantId is { } tenant && registro.TenantId != tenant)
                    throw new InvalidOperationException($"{entrada.Metadata.ClrType.Name} de outro tenant não pode ser gravado neste contexto.");
            }
            else if (entrada.State == EntityState.Modified && entrada.Property(nameof(IPertenceAoTenant.TenantId)).IsModified)
            {
                throw new InvalidOperationException($"O TenantId de {entrada.Metadata.ClrType.Name} não pode ser alterado.");
            }
        }
    }

    private sealed class ParaUtc() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v);
}
