namespace CampoSeguro.Manager.Api.Catalogos;

public sealed record DadosCatalogo(string? Nome, string? Descricao);

public sealed record ResumoVersao(int Numero, DateTimeOffset? PublicadaEm, int QuantidadeItens);

/// <summary><see cref="Publicada"/> é o que o app baixa; <see cref="Rascunho"/>, o que o gestor está editando.</summary>
public sealed record ResumoCatalogo(Guid Id, string Nome, string? Descricao, bool Ativo, ResumoVersao? Publicada, ResumoVersao? Rascunho);

public sealed record DetalheCatalogo(Guid Id, string Nome, string? Descricao, bool Ativo, IReadOnlyList<ResumoVersao> Versoes);

/// <summary><see cref="ItemId"/> é a identidade estável do item, a mesma em todas as versões em que ele aparece.</summary>
public sealed record ItemVersao(Guid ItemId, int Ordem, string Texto);

public sealed record Versao(int Numero, DateTimeOffset? PublicadaEm, IReadOnlyList<ItemVersao> Itens);

/// <summary>
/// Sem <see cref="ItemId"/>, é um item novo. Com ele, é um item que já existe neste catálogo, mesmo
/// que o texto mude: assim "itens mais não conformes" continua somando antes e depois da correção.
/// </summary>
public sealed record ItemRascunho(Guid? ItemId, string? Texto);

/// <summary>A lista completa do rascunho, na ordem desejada. Substitui a anterior.</summary>
public sealed record EdicaoRascunho(IReadOnlyList<ItemRascunho>? Itens);

public sealed record ResumoModelo(string Codigo, string Nome, string Descricao, int QuantidadeItens);
