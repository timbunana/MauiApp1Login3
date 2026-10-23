using CampoSeguro.Domain;

namespace CampoSeguro.Manager.Api.Aprs;

/// <param name="Nome">Nome congelado na assinatura: continua o mesmo depois de o técnico ser anonimizado.</param>
public sealed record Autor(Guid Id, string Nome);

public sealed record CatalogoDaApr(Guid Id, string Nome, int Versao);

public sealed record ResumoApr(
    Guid Id, DateTimeOffset AssinadaEm, DateTimeOffset RecebidaEm, Autor Autor, CatalogoDaApr Catalogo, string? Local,
    int Itens, int NaoConformes, int NaoAplica, int Fotos, int FotosRecebidas, bool PdfDisponivel);

public sealed record PaginaDeAprs(IReadOnlyList<ResumoApr> Aprs, int Total, int Pagina, int Tamanho);

public sealed record LocalDaApr(double? Latitude, double? Longitude, double? PrecisaoMetros, string? Descricao);

public sealed record ItemDaApr(Guid ItemId, int Ordem, string Texto, Resposta Resposta, string? Observacao);

public enum SituacaoArquivo
{
    /// <summary>Registrada na APR, mas o aparelho ainda não subiu o arquivo.</summary>
    Pendente,

    Disponivel,

    /// <summary>Apagada pela política de retenção; o registro e o hash continuam.</summary>
    Expurgada,
}

public sealed record FotoDaApr(Guid Id, DateTimeOffset CapturadaEm, int Largura, int Altura, SituacaoArquivo Situacao);

/// <param name="ConteudoHash">SHA-256 do conteúdo assinado, em hexadecimal: é o que permite provar que a APR não mudou.</param>
public sealed record DetalheApr(
    Guid Id, DateTimeOffset AssinadaEm, DateTimeOffset RecebidaEm, Autor Autor, CatalogoDaApr Catalogo,
    LocalDaApr Local, string? Observacao, string ConteudoHash, string? Dispositivo, bool PdfDisponivel,
    IReadOnlyList<ItemDaApr> Itens, IReadOnlyList<FotoDaApr> Fotos);
