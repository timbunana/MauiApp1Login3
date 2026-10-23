using System.Security.Cryptography;
using CampoSeguro.Data;
using CampoSeguro.Data.Arquivos;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Manager.Api.Aprs;

/// <summary>
/// Consulta das APRs que os técnicos da empresa sincronizaram. Só leitura: a APR assinada não muda
/// (trigger no banco). Fotos e PDF saem só por aqui, para quem é da empresa, e conferidos contra o hash.
/// </summary>
public static class AprsEndpoints
{
    public const int TamanhoMaximoPagina = 100;

    public static void MapAprs(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/aprs").WithTags("APRs");

        grupo.MapGet("/", Listar);
        grupo.MapGet("/{id:guid}", Detalhar);
        grupo.MapGet("/{id:guid}/pdf", Pdf);
        grupo.MapGet("/{id:guid}/evidencias/{evidenciaId:guid}", Foto);
    }

    /// <summary>Mais recentes primeiro. <paramref name="de"/> inclusivo, <paramref name="ate"/> exclusivo, pela hora da assinatura.</summary>
    private static async Task<IResult> Listar(DateTimeOffset? de, DateTimeOffset? ate, Guid? tecnicoId, Guid? catalogoId,
        bool? somenteNaoConformes, int? pagina, int? tamanho, CampoSeguroDbContext db, CancellationToken ct)
    {
        var numero = Math.Max(pagina ?? 1, 1);
        var porPagina = Math.Clamp(tamanho ?? 20, 1, TamanhoMaximoPagina);

        var consulta = db.Aprs.AsNoTracking();
        if (de is not null)
            consulta = consulta.Where(a => a.AssinadaEm >= de);
        if (ate is not null)
            consulta = consulta.Where(a => a.AssinadaEm < ate);
        if (tecnicoId is not null)
            consulta = consulta.Where(a => a.AutorId == tecnicoId);
        if (catalogoId is not null)
            consulta = consulta.Where(a => a.CatalogoVersao!.CatalogoId == catalogoId);
        if (somenteNaoConformes == true)
            consulta = consulta.Where(a => a.Itens.Any(i => i.Resposta == Resposta.NaoConforme));

        var total = await consulta.CountAsync(ct);
        var aprs = await consulta
            .OrderByDescending(a => a.AssinadaEm).ThenBy(a => a.Id)
            .Skip((numero - 1) * porPagina).Take(porPagina)
            .Select(a => new ResumoApr(
                a.Id, a.AssinadaEm, a.RecebidaEm,
                new Autor(a.AutorId, a.AutorNome),
                new CatalogoDaApr(a.CatalogoVersao!.CatalogoId, a.CatalogoVersao.Catalogo!.Nome, a.CatalogoVersao.Numero),
                a.LocalDescricao,
                a.Itens.Count,
                a.Itens.Count(i => i.Resposta == Resposta.NaoConforme),
                a.Itens.Count(i => i.Resposta == Resposta.NaoAplica),
                a.Evidencias.Count,
                a.Evidencias.Count(e => e.RecebidaEm != null && e.ExpurgadaEm == null),
                a.PdfHash != null))
            .ToListAsync(ct);

        return Results.Ok(new PaginaDeAprs(aprs, total, numero, porPagina));
    }

    private static async Task<IResult> Detalhar(Guid id, CampoSeguroDbContext db, CancellationToken ct)
    {
        var apr = await db.Aprs.AsNoTracking()
            .Include(a => a.Itens)
            .Include(a => a.Evidencias)
            .Include(a => a.CatalogoVersao!).ThenInclude(v => v.Catalogo)
            .Include(a => a.SessaoDispositivo)
            .AsSplitQuery()
            .SingleOrDefaultAsync(a => a.Id == id, ct);
        if (apr is null)
            return AprNaoEncontrada();

        var versao = apr.CatalogoVersao!;
        return Results.Ok(new DetalheApr(
            apr.Id, apr.AssinadaEm, apr.RecebidaEm, new Autor(apr.AutorId, apr.AutorNome),
            new CatalogoDaApr(versao.CatalogoId, versao.Catalogo!.Nome, versao.Numero),
            new LocalDaApr(apr.Latitude, apr.Longitude, apr.PrecisaoMetros, apr.LocalDescricao),
            apr.Observacao, Convert.ToHexStringLower(apr.ConteudoHash), apr.SessaoDispositivo?.Dispositivo, apr.PdfHash is not null,
            apr.Itens.OrderBy(i => i.Ordem).Select(i => new ItemDaApr(i.CatalogoItemId, i.Ordem, i.Texto, i.Resposta, i.Observacao)).ToList(),
            apr.Evidencias.OrderBy(e => e.CapturadaEm).ThenBy(e => e.Id)
                .Select(e => new FotoDaApr(e.Id, e.CapturadaEm, e.Largura, e.Altura, Situacao(e))).ToList()));
    }

    private static async Task<IResult> Pdf(Guid id, CampoSeguroDbContext db, Armazenamento armazenamento, HttpContext http,
        ILoggerFactory logs, CancellationToken ct)
    {
        var apr = await db.Aprs.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (apr is null)
            return AprNaoEncontrada();
        if (apr.PdfCaminho is null || apr.PdfHash is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "O PDF desta APR ainda não foi sincronizado.");

        // Horário de Brasília (sem horário de verão desde 2019) no nome do arquivo, que é para gente ler.
        var nome = $"APR {apr.AssinadaEm.ToOffset(TimeSpan.FromHours(-3)):yyyy-MM-dd HHmm} {apr.AutorNome}.pdf";
        return await Servir(armazenamento, apr.PdfCaminho, apr.PdfHash, "application/pdf", nome, http, logs, ct);
    }

    private static async Task<IResult> Foto(Guid id, Guid evidenciaId, CampoSeguroDbContext db, Armazenamento armazenamento,
        HttpContext http, ILoggerFactory logs, CancellationToken ct)
    {
        var foto = await db.AprEvidencias.AsNoTracking().SingleOrDefaultAsync(e => e.Id == evidenciaId && e.AprId == id, ct);
        if (foto is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Foto não encontrada.");

        return Situacao(foto) switch
        {
            SituacaoArquivo.Pendente => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "A foto ainda não foi sincronizada pelo aparelho."),
            SituacaoArquivo.Expurgada => Results.Problem(statusCode: StatusCodes.Status410Gone, title: "A foto foi apagada pela política de retenção."),
            _ => await Servir(armazenamento, foto.Caminho!, foto.Hash, "image/jpeg", null, http, logs, ct),
        };
    }

    /// <summary>
    /// Confere o arquivo contra o hash do banco antes de entregar. Um arquivo trocado ou corrompido no
    /// disco vira erro (e log), nunca um documento de segurança diferente do que foi assinado.
    /// </summary>
    private static async Task<IResult> Servir(Armazenamento armazenamento, string caminho, byte[] hashEsperado, string tipo,
        string? nomeDownload, HttpContext http, ILoggerFactory logs, CancellationToken ct)
    {
        var log = logs.CreateLogger(nameof(AprsEndpoints));
        if (!armazenamento.Existe(caminho))
        {
            log.LogError("Arquivo {Caminho} registrado no banco não existe no armazenamento", caminho);
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Arquivo indisponível no servidor.");
        }

        byte[] conteudo;
        await using (var arquivo = armazenamento.Abrir(caminho))
        using (var memoria = new MemoryStream())
        {
            await arquivo.CopyToAsync(memoria, ct);
            conteudo = memoria.ToArray();
        }

        if (!SHA256.HashData(conteudo).AsSpan().SequenceEqual(hashEsperado))
        {
            log.LogError("Arquivo {Caminho} não confere com o hash registrado", caminho);
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "O arquivo armazenado não confere com o registrado.");
        }

        // Documento de uma empresa: nenhum cache compartilhado (proxy, CDN) pode guardar.
        http.Response.Headers.CacheControl = "private, no-store";
        return Results.File(conteudo, tipo, nomeDownload);
    }

    private static SituacaoArquivo Situacao(AprEvidencia e) =>
        e.ExpurgadaEm is not null ? SituacaoArquivo.Expurgada
        : e.RecebidaEm is not null && e.Caminho is not null ? SituacaoArquivo.Disponivel
        : SituacaoArquivo.Pendente;

    private static IResult AprNaoEncontrada() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "APR não encontrada.");
}
