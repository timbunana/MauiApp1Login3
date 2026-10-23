using System.Security.Cryptography;

namespace CampoSeguro.Data.Arquivos;

/// <summary>
/// Fotos e PDFs das APRs em disco, <b>particionados por tenant</b> (a primeira pasta é o id da empresa).
/// Os caminhos são sempre montados aqui, a partir de ids, nunca recebidos do cliente; e os arquivos
/// só saem por endpoint autorizado — o filtro de tenant do EF não protege o sistema de arquivos.
/// </summary>
public sealed class Armazenamento
{
    private readonly string raiz;

    public Armazenamento(string raiz)
    {
        if (string.IsNullOrWhiteSpace(raiz))
            throw new InvalidOperationException("Defina Armazenamento__Raiz com a pasta dos arquivos.");

        this.raiz = Path.GetFullPath(raiz);
        Directory.CreateDirectory(Path.Combine(this.raiz, PastaRecebendo));
    }

    /// <summary>Uploads em andamento. Fica dentro da raiz para que mover para o destino seja um rename.</summary>
    private const string PastaRecebendo = ".recebendo";

    public static string CaminhoEvidencia(Guid tenant, Guid apr, Guid evidencia) => $"{tenant:N}/aprs/{apr:N}/evidencias/{evidencia:N}.jpg";

    public static string CaminhoPdf(Guid tenant, Guid apr) => $"{tenant:N}/aprs/{apr:N}/apr.pdf";

    /// <summary>
    /// Grava o corpo num arquivo temporário, calculando o SHA-256 no caminho. Nada fica visível até
    /// <see cref="Confirmar"/>: um upload interrompido ou recusado não deixa arquivo pela metade no destino.
    /// </summary>
    /// <returns>Nulo se o conteúdo passar de <paramref name="limiteBytes"/>.</returns>
    public async Task<ArquivoRecebido?> Receber(Stream origem, long limiteBytes, CancellationToken ct)
    {
        var temporario = Path.Combine(raiz, PastaRecebendo, Guid.NewGuid().ToString("N"));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        var inicio = new byte[8];
        var inicioLido = 0;

        await using (var destino = File.Create(temporario))
        {
            int lidos;
            while ((lidos = await origem.ReadAsync(buffer, ct)) > 0)
            {
                total += lidos;
                if (total > limiteBytes)
                {
                    await destino.DisposeAsync();
                    File.Delete(temporario);
                    return null;
                }

                if (inicioLido < inicio.Length)
                {
                    var copiar = Math.Min(inicio.Length - inicioLido, lidos);
                    Array.Copy(buffer, 0, inicio, inicioLido, copiar);
                    inicioLido += copiar;
                }

                hash.AppendData(buffer, 0, lidos);
                await destino.WriteAsync(buffer.AsMemory(0, lidos), ct);
            }
        }

        return new ArquivoRecebido(temporario, total, hash.GetHashAndReset(), inicio[..inicioLido]);
    }

    /// <summary>Move o temporário para o destino definitivo, substituindo uma tentativa anterior.</summary>
    public void Confirmar(ArquivoRecebido arquivo, string relativo)
    {
        var destino = Completo(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.Move(arquivo.Temporario, destino, overwrite: true);
    }

    public static void Descartar(ArquivoRecebido arquivo)
    {
        if (File.Exists(arquivo.Temporario))
            File.Delete(arquivo.Temporario);
    }

    public Stream Abrir(string relativo) => File.OpenRead(Completo(relativo));

    public bool Existe(string relativo) => File.Exists(Completo(relativo));

    /// <summary>Defesa extra: mesmo com caminhos montados aqui, nada resolve para fora da raiz.</summary>
    private string Completo(string relativo)
    {
        var completo = Path.GetFullPath(Path.Combine(raiz, relativo));
        if (!completo.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Caminho fora da raiz do armazenamento.");
        return completo;
    }
}

/// <param name="Inicio">Primeiros bytes, para conferir o tipo do arquivo (assinatura JPEG ou PDF).</param>
public sealed record ArquivoRecebido(string Temporario, long TamanhoBytes, byte[] Hash, byte[] Inicio)
{
    public bool PareceJpeg => Inicio is [0xFF, 0xD8, 0xFF, ..];

    public bool ParecePdf => Inicio is [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', ..];
}
