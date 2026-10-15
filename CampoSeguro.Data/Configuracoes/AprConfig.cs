using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampoSeguro.Data.Configuracoes;

internal sealed class AprConfig : IEntityTypeConfiguration<Apr>
{
    public void Configure(EntityTypeBuilder<Apr> b)
    {
        b.HasAlternateKey(a => new { a.TenantId, a.Id });

        // O Id vem do aparelho; o banco nunca gera.
        b.Property(a => a.Id).ValueGeneratedNever();

        b.Property(a => a.AutorNome).HasMaxLength(Tamanhos.Nome);
        b.Property(a => a.LocalDescricao).HasMaxLength(Tamanhos.TextoItem);
        b.Property(a => a.Observacao).HasMaxLength(Tamanhos.TextoLongo);
        b.Property(a => a.PdfCaminho).HasMaxLength(Tamanhos.Caminho);

        b.HasOne(a => a.Autor).WithMany()
            .HasForeignKey(a => new { a.TenantId, a.AutorId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(a => a.CatalogoVersao).WithMany()
            .HasForeignKey(a => new { a.TenantId, a.CatalogoVersaoId })
            .HasPrincipalKey(v => new { v.TenantId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(a => a.SessaoDispositivo).WithMany()
            .HasForeignKey(a => new { a.TenantId, a.SessaoDispositivoId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Listagens do painel: por período e por técnico.
        b.HasIndex(a => new { a.TenantId, a.AssinadaEm });
        b.HasIndex(a => new { a.TenantId, a.AutorId, a.AssinadaEm });
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_apr_conteudo_hash", "octet_length(conteudo_hash) = 32");
            t.HasCheckConstraint("ck_apr_pdf_hash", "pdf_hash IS NULL OR octet_length(pdf_hash) = 32");
            t.HasCheckConstraint("ck_apr_pdf", "pdf_caminho IS NULL OR pdf_hash IS NOT NULL");
            t.HasCheckConstraint("ck_apr_coordenadas", "(latitude IS NULL) = (longitude IS NULL)");
            t.HasCheckConstraint("ck_apr_latitude", "latitude BETWEEN -90 AND 90");
            t.HasCheckConstraint("ck_apr_longitude", "longitude BETWEEN -180 AND 180");
            t.HasCheckConstraint("ck_apr_precisao", "precisao_metros >= 0");
        });
    }
}

internal sealed class AprItemConfig : IEntityTypeConfiguration<AprItem>
{
    public void Configure(EntityTypeBuilder<AprItem> b)
    {
        b.Property(i => i.Texto).HasMaxLength(Tamanhos.TextoItem);
        b.Property(i => i.Observacao).HasMaxLength(Tamanhos.TextoLongo);

        b.HasOne(i => i.Apr).WithMany(a => a.Itens)
            .HasForeignKey(i => new { i.TenantId, i.AprId })
            .HasPrincipalKey(a => new { a.TenantId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(i => i.CatalogoItem).WithMany()
            .HasForeignKey(i => new { i.TenantId, i.CatalogoItemId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(i => new { i.AprId, i.Ordem }).IsUnique();
        b.HasIndex(i => new { i.AprId, i.CatalogoItemId }).IsUnique();

        // Indicador "itens mais não conformes" agrupa por item de origem, não por texto.
        b.HasIndex(i => new { i.TenantId, i.CatalogoItemId, i.Resposta });

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_apr_item_ordem", "ordem >= 1");
            t.HasCheckConstraint("ck_apr_item_resposta", "resposta IN ('Conforme', 'NaoConforme', 'NaoAplica')");
        });
    }
}

internal sealed class AprEvidenciaConfig : IEntityTypeConfiguration<AprEvidencia>
{
    public void Configure(EntityTypeBuilder<AprEvidencia> b)
    {
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Caminho).HasMaxLength(Tamanhos.Caminho);

        b.HasOne(e => e.Apr).WithMany(a => a.Evidencias)
            .HasForeignKey(e => new { e.TenantId, e.AprId })
            .HasPrincipalKey(a => new { a.TenantId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_apr_evidencia_hash", "octet_length(hash) = 32");
            t.HasCheckConstraint("ck_apr_evidencia_dimensoes", "tamanho_bytes > 0 AND largura > 0 AND altura > 0");
            t.HasCheckConstraint("ck_apr_evidencia_recebida", "caminho IS NULL OR recebida_em IS NOT NULL");
            t.HasCheckConstraint("ck_apr_evidencia_expurgo", "expurgada_em IS NULL OR (caminho IS NULL AND recebida_em IS NOT NULL)");
        });
    }
}
