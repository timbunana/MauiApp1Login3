using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampoSeguro.Data.Configuracoes;

internal sealed class CatalogoConfig : IEntityTypeConfiguration<Catalogo>
{
    public void Configure(EntityTypeBuilder<Catalogo> b)
    {
        b.HasAlternateKey(c => new { c.TenantId, c.Id });
        b.Property(c => c.Nome).HasMaxLength(Tamanhos.Nome);
        b.Property(c => c.Descricao).HasMaxLength(Tamanhos.TextoLongo);
        b.HasIndex(c => new { c.TenantId, c.Nome }).IsUnique();
    }
}

internal sealed class CatalogoItemConfig : IEntityTypeConfiguration<CatalogoItem>
{
    public void Configure(EntityTypeBuilder<CatalogoItem> b)
    {
        b.HasAlternateKey(i => new { i.TenantId, i.Id });

        b.HasOne(i => i.Catalogo).WithMany(c => c.Itens)
            .HasForeignKey(i => new { i.TenantId, i.CatalogoId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CatalogoVersaoConfig : IEntityTypeConfiguration<CatalogoVersao>
{
    public void Configure(EntityTypeBuilder<CatalogoVersao> b)
    {
        b.HasAlternateKey(v => new { v.TenantId, v.Id });

        b.HasOne(v => v.Catalogo).WithMany(c => c.Versoes)
            .HasForeignKey(v => new { v.TenantId, v.CatalogoId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(v => new { v.CatalogoId, v.Numero }).IsUnique();

        // No máximo um rascunho por catálogo; publicar fecha o rascunho e a próxima edição abre outro.
        b.HasIndex(v => v.CatalogoId).IsUnique()
            .HasFilter("publicada_em IS NULL")
            .HasDatabaseName("ux_catalogo_versao_rascunho");

        b.ToTable(t => t.HasCheckConstraint("ck_catalogo_versao_numero", "numero >= 1"));
    }
}

internal sealed class CatalogoVersaoItemConfig : IEntityTypeConfiguration<CatalogoVersaoItem>
{
    public void Configure(EntityTypeBuilder<CatalogoVersaoItem> b)
    {
        b.Property(i => i.Texto).HasMaxLength(Tamanhos.TextoItem);

        b.HasOne(i => i.CatalogoVersao).WithMany(v => v.Itens)
            .HasForeignKey(i => new { i.TenantId, i.CatalogoVersaoId })
            .HasPrincipalKey(v => new { v.TenantId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(i => i.CatalogoItem).WithMany()
            .HasForeignKey(i => new { i.TenantId, i.CatalogoItemId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(i => new { i.CatalogoVersaoId, i.Ordem }).IsUnique();
        b.HasIndex(i => new { i.CatalogoVersaoId, i.CatalogoItemId }).IsUnique();

        b.ToTable(t => t.HasCheckConstraint("ck_catalogo_versao_item_ordem", "ordem >= 1"));
    }
}
