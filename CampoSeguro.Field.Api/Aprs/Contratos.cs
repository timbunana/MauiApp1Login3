using CampoSeguro.Domain;

namespace CampoSeguro.Field.Api.Aprs;

// Campos anuláveis de propósito: um JSON incompleto chega até a validação e volta como 400 com o
// campo que faltou, em vez de falhar na desserialização.

/// <param name="ConteudoHash">SHA-256 de <see cref="ConteudoApr.Canonico"/>, em hexadecimal.</param>
public sealed record AprEnviada(
    Guid? CatalogoVersaoId, DateTimeOffset? AssinadaEm, string? ConteudoHash,
    double? Latitude, double? Longitude, double? PrecisaoMetros, string? LocalDescricao, string? Observacao,
    IReadOnlyList<ItemEnviado>? Itens, IReadOnlyList<EvidenciaEnviada>? Evidencias);

public sealed record ItemEnviado(Guid? CatalogoItemId, int? Ordem, string? Texto, Resposta? Resposta, string? Observacao);

/// <param name="Id">Gerado no aparelho, como o da APR.</param>
/// <param name="Hash">SHA-256 do arquivo já comprimido, em hexadecimal.</param>
public sealed record EvidenciaEnviada(Guid? Id, string? Hash, long? TamanhoBytes, int? Largura, int? Altura, DateTimeOffset? CapturadaEm);

/// <summary>O que ainda falta subir. O app repete os uploads até as duas listas esvaziarem.</summary>
public sealed record SituacaoApr(Guid Id, DateTimeOffset RecebidaEm, IReadOnlyList<Guid> EvidenciasPendentes, bool PdfPendente);
