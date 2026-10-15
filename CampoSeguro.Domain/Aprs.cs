namespace CampoSeguro.Domain;

/// <summary>
/// APR assinada. O rascunho existe só no SQLite do aparelho: o servidor recebe a APR já assinada,
/// e a partir daí ela não muda (itens imutáveis por trigger, exclusão bloqueada).
/// O <see cref="Entidade.Id"/> é gerado no celular, o que torna o reenvio idempotente.
/// </summary>
public class Apr : EntidadeDoTenant
{
    public Guid AutorId { get; set; }

    /// <summary>Nome do autor congelado na assinatura; sobrevive à anonimização do usuário.</summary>
    public required string AutorNome { get; set; }

    public Guid CatalogoVersaoId { get; set; }

    /// <summary>Horário do aparelho no momento da assinatura.</summary>
    public DateTimeOffset AssinadaEm { get; set; }

    /// <summary>Horário do servidor ao receber; a diferença para AssinadaEm mostra o atraso de sincronização.</summary>
    public DateTimeOffset RecebidaEm { get; set; }

    /// <summary>Sessão do aparelho que enviou a APR.</summary>
    public Guid? SessaoDispositivoId { get; set; }

    /// <summary>SHA-256 do conteúdo canônico calculado no aparelho ao assinar (32 bytes).</summary>
    public required byte[] ConteudoHash { get; set; }

    // Local: coordenadas são opcionais (o GPS pode falhar); a descrição é o que o técnico digita.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? PrecisaoMetros { get; set; }
    public string? LocalDescricao { get; set; }

    public string? Observacao { get; set; }

    /// <summary>PDF gerado no aparelho; chega depois da APR, por isso é anulável.</summary>
    public string? PdfCaminho { get; set; }
    public byte[]? PdfHash { get; set; }

    public Usuario? Autor { get; set; }
    public CatalogoVersao? CatalogoVersao { get; set; }
    public SessaoDispositivo? SessaoDispositivo { get; set; }
    public List<AprItem> Itens { get; set; } = [];
    public List<AprEvidencia> Evidencias { get; set; } = [];
}

/// <summary>
/// Cópia congelada de cada item no momento da assinatura. Editar o catálogo depois não altera
/// o que esta APR diz — é o que a torna auditável.
/// </summary>
public class AprItem : EntidadeDoTenant
{
    public Guid AprId { get; set; }

    /// <summary>Origem do item, para indicadores que atravessam revisões de texto.</summary>
    public Guid CatalogoItemId { get; set; }

    public int Ordem { get; set; }
    public required string Texto { get; set; }
    public Resposta Resposta { get; set; }
    public string? Observacao { get; set; }

    public Apr? Apr { get; set; }
    public CatalogoItem? CatalogoItem { get; set; }
}

/// <summary>
/// Foto da APR. O registro chega com a APR (Id gerado no celular, hash e metadados);
/// o arquivo sobe depois, e só então <see cref="Caminho"/> e <see cref="RecebidaEm"/> são preenchidos.
/// </summary>
public class AprEvidencia : EntidadeDoTenant
{
    public Guid AprId { get; set; }

    /// <summary>SHA-256 do arquivo já comprimido (32 bytes); confere a integridade no upload.</summary>
    public required byte[] Hash { get; set; }

    public long TamanhoBytes { get; set; }
    public int Largura { get; set; }
    public int Altura { get; set; }
    public DateTimeOffset CapturadaEm { get; set; }

    /// <summary>Caminho relativo à raiz do armazenamento do tenant. Nunca é servido diretamente.</summary>
    public string? Caminho { get; set; }
    public DateTimeOffset? RecebidaEm { get; set; }

    /// <summary>Arquivo apagado pela política de retenção; o registro e o hash permanecem.</summary>
    public DateTimeOffset? ExpurgadaEm { get; set; }

    public Apr? Apr { get; set; }
}
