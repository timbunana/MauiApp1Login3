using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampoSeguro.Data.Configuracoes;

internal sealed class TenantConfig : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.Property(t => t.Nome).HasMaxLength(Tamanhos.Nome);
        b.Property(t => t.Documento).HasMaxLength(14);
        b.HasIndex(t => t.Documento).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("ck_tenant_documento", "documento ~ '^[0-9]{14}$'"));
    }
}

internal sealed class AdministradorPlataformaConfig : IEntityTypeConfiguration<AdministradorPlataforma>
{
    public void Configure(EntityTypeBuilder<AdministradorPlataforma> b)
    {
        b.Property(a => a.Nome).HasMaxLength(Tamanhos.Nome);
        b.Property(a => a.Email).HasMaxLength(Tamanhos.Email);
        b.Property(a => a.EmailNormalizado).HasMaxLength(Tamanhos.Email);
        b.Property(a => a.SenhaHash).HasMaxLength(Tamanhos.SenhaHash);
        b.HasIndex(a => a.EmailNormalizado).IsUnique();
    }
}

internal sealed class UsuarioConfig : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.HasAlternateKey(u => new { u.TenantId, u.Id });

        b.Property(u => u.Nome).HasMaxLength(Tamanhos.Nome);
        b.Property(u => u.Email).HasMaxLength(Tamanhos.Email);
        b.Property(u => u.EmailNormalizado).HasMaxLength(Tamanhos.Email);
        b.Property(u => u.SenhaHash).HasMaxLength(Tamanhos.SenhaHash);

        // Único só entre ativos e entre todos os tenants: o login é por e-mail e precisa achar
        // uma pessoa só; ao desativar, a empresa anterior libera o e-mail para a próxima.
        b.HasIndex(u => u.EmailNormalizado).IsUnique().HasFilter("ativo");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_usuario_desativado", "ativo OR desativado_em IS NOT NULL");
            t.HasCheckConstraint("ck_usuario_anonimizado", "anonimizado_em IS NULL OR NOT ativo");
        });
    }
}

internal sealed class ConviteConfig : IEntityTypeConfiguration<Convite>
{
    public void Configure(EntityTypeBuilder<Convite> b)
    {
        b.HasOne(c => c.Usuario).WithMany()
            .HasForeignKey(c => new { c.TenantId, c.UsuarioId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(c => c.TokenHash).IsUnique();

        // Um convite pendente por usuário e tipo: reenviar exige revogar o anterior,
        // senão dois links válidos ficariam circulando.
        b.HasIndex(c => new { c.UsuarioId, c.Tipo }).IsUnique()
            .HasFilter("usado_em IS NULL AND revogado_em IS NULL")
            .HasDatabaseName("ux_convite_pendente");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_convite_token_hash", "octet_length(token_hash) = 32");
            t.HasCheckConstraint("ck_convite_expira", "expira_em > criado_em");
        });
    }
}

internal sealed class SessaoDispositivoConfig : IEntityTypeConfiguration<SessaoDispositivo>
{
    public void Configure(EntityTypeBuilder<SessaoDispositivo> b)
    {
        b.HasAlternateKey(s => new { s.TenantId, s.Id });

        b.HasOne(s => s.Usuario).WithMany()
            .HasForeignKey(s => new { s.TenantId, s.UsuarioId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(s => s.Dispositivo).HasMaxLength(200);
        b.HasIndex(s => s.TokenHash).IsUnique();

        b.ToTable(t => t.HasCheckConstraint("ck_sessao_dispositivo_token_hash", "octet_length(token_hash) = 32"));
    }
}
