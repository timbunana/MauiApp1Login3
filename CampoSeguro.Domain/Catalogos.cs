namespace CampoSeguro.Domain;

/// <summary>
/// Conjunto de verificação de uma atividade, por tenant. Ex.: "Telecom — Altura".
/// O catálogo define a atividade; a APR não guarda um "tipo" separado que poderia divergir dele.
/// </summary>
public class Catalogo : EntidadeDoTenant
{
    public required string Nome { get; set; }
    public string? Descricao { get; set; }

    /// <summary>Catálogo inativo some do app, mas suas versões continuam referenciadas pelas APRs.</summary>
    public bool Ativo { get; set; } = true;

    public List<CatalogoVersao> Versoes { get; set; } = [];
    public List<CatalogoItem> Itens { get; set; } = [];
}

/// <summary>
/// Identidade estável de um item ao longo das versões. Não tem texto: o texto pertence à versão.
/// É a chave usada para contar "itens mais não conformes" mesmo depois de o texto ser revisado.
/// Nunca é excluído; deixa de aparecer simplesmente não entrando nas versões novas.
/// </summary>
public class CatalogoItem : EntidadeDoTenant
{
    public Guid CatalogoId { get; set; }

    public Catalogo? Catalogo { get; set; }
}

/// <summary>
/// Retrato do catálogo. Enquanto <see cref="PublicadaEm"/> é nulo é um rascunho editável;
/// depois de publicada é imutável (garantido por trigger) e é o que o app baixa e cacheia.
/// </summary>
public class CatalogoVersao : EntidadeDoTenant
{
    public Guid CatalogoId { get; set; }

    /// <summary>Sequencial por catálogo, a partir de 1.</summary>
    public int Numero { get; set; }

    public DateTimeOffset? PublicadaEm { get; set; }

    public Catalogo? Catalogo { get; set; }
    public List<CatalogoVersaoItem> Itens { get; set; } = [];
}

/// <summary>Item tal como aparece numa versão específica do catálogo.</summary>
public class CatalogoVersaoItem : EntidadeDoTenant
{
    public Guid CatalogoVersaoId { get; set; }
    public Guid CatalogoItemId { get; set; }
    public int Ordem { get; set; }
    public required string Texto { get; set; }

    public CatalogoVersao? CatalogoVersao { get; set; }
    public CatalogoItem? CatalogoItem { get; set; }
}
