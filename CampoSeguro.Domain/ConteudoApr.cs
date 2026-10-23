using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace CampoSeguro.Domain;

/// <summary>
/// Forma canônica do conteúdo de uma APR assinada e o SHA-256 dela (<see cref="Apr.ConteudoHash"/>).
/// <b>O app e o servidor precisam usar este mesmo código</b>: o app calcula ao assinar, o servidor
/// recalcula ao receber e recusa a APR se não bater. Qualquer mudança aqui muda o hash de toda APR
/// nova — trate como mudança de contrato, com versão.
/// </summary>
public static class ConteudoApr
{
    /// <summary>Muda junto com qualquer alteração no formato abaixo.</summary>
    public const int VersaoFormato = 1;

    public sealed record Item(Guid CatalogoItemId, int Ordem, string Texto, Resposta Resposta, string? Observacao);

    /// <param name="Hash">SHA-256 do arquivo da foto, em hexadecimal minúsculo.</param>
    public sealed record Evidencia(Guid Id, string Hash);

    public sealed record Conteudo(
        Guid AprId, Guid AutorId, Guid CatalogoVersaoId, DateTimeOffset AssinadaEm,
        double? Latitude, double? Longitude, double? PrecisaoMetros, string? LocalDescricao, string? Observacao,
        IReadOnlyList<Item> Itens, IReadOnlyList<Evidencia> Evidencias);

    public static byte[] Hash(Conteudo conteudo) => SHA256.HashData(Canonico(conteudo));

    /// <summary>
    /// JSON compacto, com campos em ordem fixa, itens por ordem e evidências por id. O horário vai em
    /// UTC com microssegundos, a precisão que o PostgreSQL guarda; textos vão exatamente como enviados.
    /// </summary>
    public static byte[] Canonico(Conteudo c) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        formato = VersaoFormato,
        apr = c.AprId,
        autor = c.AutorId,
        versao = c.CatalogoVersaoId,
        assinadaEm = c.AssinadaEm.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
        latitude = c.Latitude,
        longitude = c.Longitude,
        precisaoMetros = c.PrecisaoMetros,
        local = c.LocalDescricao,
        observacao = c.Observacao,
        itens = c.Itens.OrderBy(i => i.Ordem).Select(i => new
        {
            item = i.CatalogoItemId,
            ordem = i.Ordem,
            texto = i.Texto,
            resposta = i.Resposta.ToString(),
            observacao = i.Observacao,
        }),
        evidencias = c.Evidencias.OrderBy(e => e.Id).Select(e => new { id = e.Id, hash = e.Hash.ToLowerInvariant() }),
    });
}
