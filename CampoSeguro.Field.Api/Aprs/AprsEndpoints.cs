using CampoSeguro.Data;
using CampoSeguro.Data.Arquivos;
using CampoSeguro.Data.Configuracoes;
using CampoSeguro.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Field.Api.Aprs;

/// <summary>
/// Sincronização das APRs assinadas no aparelho. Tudo é idempotente pelo id gerado no celular:
/// a fila do app pode repetir qualquer chamada sem medo de duplicar. A ordem é APR → fotos → PDF;
/// a resposta de cada passo diz o que ainda falta.
/// </summary>
public static class AprsEndpoints
{
    public const int MaximoEvidencias = 20;

    /// <summary>A foto chega comprimida no app (~1280px, 200–400 KB); o limite só barra quem não comprimiu.</summary>
    public const long MaximoBytesFoto = 5 * 1024 * 1024;

    public const long MaximoBytesPdf = 20 * 1024 * 1024;

    public static void MapAprs(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/aprs").WithTags("APRs");

        grupo.MapPut("/{id:guid}", Receber);
        grupo.MapGet("/{id:guid}", Consultar);
        grupo.MapPut("/{id:guid}/evidencias/{evidenciaId:guid}", ReceberFoto)
            .WithMetadata(new RequestSizeLimitAttribute(MaximoBytesFoto + 1));
        grupo.MapPut("/{id:guid}/pdf", ReceberPdf)
            .WithMetadata(new RequestSizeLimitAttribute(MaximoBytesPdf + 1));
    }

    /// <summary>
    /// Grava APR, itens e o registro das fotos numa única transação. Mesmo id e mesmo conteúdo:
    /// responde 200 com o que falta subir. Mesmo id e conteúdo diferente: 409.
    /// </summary>
    private static async Task<IResult> Receber(Guid id, AprEnviada pedido, CampoSeguroDbContext db,
        ContextoTenantDaRequisicao contexto, ILoggerFactory logs, CancellationToken ct)
    {
        if (Validar(pedido) is { } invalido)
            return invalido;

        var hashEnviado = Convert.FromHexString(pedido.ConteudoHash!);
        var autorId = contexto.UsuarioId!.Value;

        if (await db.Aprs.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) is { } existente)
            return await Repeticao(db, existente, autorId, hashEnviado, ct);

        var versao = await db.CatalogoVersoes.AsNoTracking().Include(v => v.Itens)
            .SingleOrDefaultAsync(v => v.Id == pedido.CatalogoVersaoId && v.PublicadaEm != null, ct);
        if (versao is null)
            return Inconsistente("A versão do catálogo não existe ou não foi publicada.");

        // A APR precisa responder cada item da versão, uma vez, com o texto e a ordem dela.
        var esperados = versao.Itens.Select(i => (i.CatalogoItemId, i.Ordem, i.Texto)).OrderBy(i => i.Ordem);
        var enviados = pedido.Itens!.Select(i => (i.CatalogoItemId!.Value, i.Ordem!.Value, i.Texto!)).OrderBy(i => i.Item2);
        if (!esperados.SequenceEqual(enviados))
            return Inconsistente("Os itens não conferem com a versão do catálogo: todos precisam ser respondidos, com o texto e a ordem publicados.");

        var conteudo = new ConteudoApr.Conteudo(
            id, autorId, versao.Id, pedido.AssinadaEm!.Value,
            pedido.Latitude, pedido.Longitude, pedido.PrecisaoMetros, pedido.LocalDescricao, pedido.Observacao,
            pedido.Itens!.Select(i => new ConteudoApr.Item(i.CatalogoItemId!.Value, i.Ordem!.Value, i.Texto!, i.Resposta!.Value, i.Observacao)).ToList(),
            (pedido.Evidencias ?? []).Select(e => new ConteudoApr.Evidencia(e.Id!.Value, e.Hash!)).ToList());
        if (!ConteudoApr.Hash(conteudo).AsSpan().SequenceEqual(hashEnviado))
            return Inconsistente("O hash não confere com o conteúdo enviado.");

        var autor = await db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == autorId, ct);
        var apr = new Apr
        {
            Id = id,
            AutorId = autorId,
            AutorNome = autor.Nome,
            CatalogoVersaoId = versao.Id,
            AssinadaEm = pedido.AssinadaEm.Value,
            RecebidaEm = DateTimeOffset.UtcNow,
            SessaoDispositivoId = contexto.SessaoId,
            ConteudoHash = hashEnviado,
            Latitude = pedido.Latitude,
            Longitude = pedido.Longitude,
            PrecisaoMetros = pedido.PrecisaoMetros,
            LocalDescricao = pedido.LocalDescricao,
            Observacao = pedido.Observacao,
            Itens = pedido.Itens!.Select(i => new AprItem
            {
                CatalogoItemId = i.CatalogoItemId!.Value,
                Ordem = i.Ordem!.Value,
                Texto = i.Texto!,
                Resposta = i.Resposta!.Value,
                Observacao = i.Observacao,
            }).ToList(),
            Evidencias = (pedido.Evidencias ?? []).Select(e => new AprEvidencia
            {
                Id = e.Id!.Value,
                Hash = Convert.FromHexString(e.Hash!),
                TamanhoBytes = e.TamanhoBytes!.Value,
                Largura = e.Largura!.Value,
                Altura = e.Altura!.Value,
                CapturadaEm = e.CapturadaEm!.Value,
            }).ToList(),
        };
        db.Aprs.Add(apr);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.ViolouUnicidade())
        {
            // Duas entregas simultâneas do mesmo id, ou um id (da APR ou de uma foto) que já existe em
            // outra empresa. No segundo caso a consulta filtrada não acha nada, e a resposta não revela de quem é.
            db.ChangeTracker.Clear();
            return await db.Aprs.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) is { } corrida
                ? await Repeticao(db, corrida, autorId, hashEnviado, ct)
                : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Identificador já utilizado. Gere outro id para esta APR ou foto.");
        }

        logs.CreateLogger(nameof(AprsEndpoints)).LogInformation(
            "APR {AprId} recebida do técnico {UsuarioId}, empresa {TenantId}", id, autorId, apr.TenantId);

        return Results.Created($"/aprs/{id}", await Situacao(db, apr, ct));
    }

    private static async Task<IResult> Consultar(Guid id, CampoSeguroDbContext db, ContextoTenantDaRequisicao contexto, CancellationToken ct)
    {
        var apr = await db.Aprs.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.AutorId == contexto.UsuarioId, ct);
        return apr is null ? AprNaoEncontrada() : Results.Ok(await Situacao(db, apr, ct));
    }

    /// <summary>
    /// O arquivo precisa ser exatamente o que a APR registrou (mesmo SHA-256 e tamanho). Reenviar uma
    /// foto já recebida responde 200 sem gravar de novo.
    /// </summary>
    private static async Task<IResult> ReceberFoto(Guid id, Guid evidenciaId, HttpRequest requisicao, CampoSeguroDbContext db,
        Armazenamento armazenamento, ContextoTenantDaRequisicao contexto, CancellationToken ct)
    {
        var evidencia = await db.AprEvidencias.Include(e => e.Apr)
            .SingleOrDefaultAsync(e => e.Id == evidenciaId && e.AprId == id && e.Apr!.AutorId == contexto.UsuarioId, ct);
        if (evidencia is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Foto não registrada nesta APR.");
        if (evidencia.RecebidaEm is not null || evidencia.ExpurgadaEm is not null)
            return Results.Ok(await Situacao(db, evidencia.Apr!, ct));
        if (requisicao.ContentType != "image/jpeg")
            return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Envie a foto como image/jpeg.");

        var arquivo = await armazenamento.Receber(requisicao.Body, evidencia.TamanhoBytes, ct);
        if (arquivo is null || arquivo.TamanhoBytes != evidencia.TamanhoBytes || !arquivo.Hash.AsSpan().SequenceEqual(evidencia.Hash) || !arquivo.PareceJpeg)
        {
            if (arquivo is not null)
                Armazenamento.Descartar(arquivo);
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "O arquivo não confere com a foto registrada na APR.");
        }

        var caminho = Armazenamento.CaminhoEvidencia(evidencia.TenantId, id, evidenciaId);
        armazenamento.Confirmar(arquivo, caminho);
        evidencia.Caminho = caminho;
        evidencia.RecebidaEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Situacao(db, evidencia.Apr!, ct));
    }

    /// <summary>
    /// O PDF é gerado no aparelho e anexado uma vez só (o banco recusa trocar). Reenviar o mesmo
    /// arquivo responde 200; um arquivo diferente, 409.
    /// </summary>
    private static async Task<IResult> ReceberPdf(Guid id, HttpRequest requisicao, CampoSeguroDbContext db,
        Armazenamento armazenamento, ContextoTenantDaRequisicao contexto, CancellationToken ct)
    {
        var apr = await db.Aprs.SingleOrDefaultAsync(a => a.Id == id && a.AutorId == contexto.UsuarioId, ct);
        if (apr is null)
            return AprNaoEncontrada();
        if (requisicao.ContentType != "application/pdf")
            return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Envie o PDF como application/pdf.");

        var arquivo = await armazenamento.Receber(requisicao.Body, MaximoBytesPdf, ct);
        if (arquivo is null || !arquivo.ParecePdf)
        {
            if (arquivo is not null)
                Armazenamento.Descartar(arquivo);
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"Envie um PDF de até {MaximoBytesPdf / 1024 / 1024} MB.");
        }

        if (apr.PdfHash is not null)
        {
            Armazenamento.Descartar(arquivo);
            return apr.PdfHash.AsSpan().SequenceEqual(arquivo.Hash)
                ? Results.Ok(await Situacao(db, apr, ct))
                : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Esta APR já tem outro PDF anexado.");
        }

        var caminho = Armazenamento.CaminhoPdf(apr.TenantId, id);
        armazenamento.Confirmar(arquivo, caminho);
        apr.PdfCaminho = caminho;
        apr.PdfHash = arquivo.Hash;
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Situacao(db, apr, ct));
    }

    /// <summary>A mesma APR chegando de novo (fila do app repetindo) é sucesso; outra APR com o mesmo id, não.</summary>
    private static async Task<IResult> Repeticao(CampoSeguroDbContext db, Apr existente, Guid autorId, byte[] hash, CancellationToken ct) =>
        existente.AutorId == autorId && existente.ConteudoHash.AsSpan().SequenceEqual(hash)
            ? Results.Ok(await Situacao(db, existente, ct))
            : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Já existe outra APR com este id.");

    private static async Task<SituacaoApr> Situacao(CampoSeguroDbContext db, Apr apr, CancellationToken ct)
    {
        var pendentes = await db.AprEvidencias.AsNoTracking()
            .Where(e => e.AprId == apr.Id && e.RecebidaEm == null && e.ExpurgadaEm == null)
            .OrderBy(e => e.Id).Select(e => e.Id).ToListAsync(ct);
        return new SituacaoApr(apr.Id, apr.RecebidaEm, pendentes, apr.PdfHash is null);
    }

    private static IResult? Validar(AprEnviada p)
    {
        var erros = new Dictionary<string, string[]>();
        void Erro(string campo, string mensagem) => erros.TryAdd(campo, [mensagem]);

        if (p.CatalogoVersaoId is null)
            Erro("catalogoVersaoId", "Informe a versão do catálogo.");
        if (p.AssinadaEm is null)
            Erro("assinadaEm", "Informe quando a APR foi assinada.");
        if (!HashValido(p.ConteudoHash))
            Erro("conteudoHash", "Informe o SHA-256 do conteúdo, em hexadecimal.");
        if ((p.Latitude is null) != (p.Longitude is null))
            Erro("latitude", "Latitude e longitude vão juntas.");
        if (p.Latitude is < -90 or > 90)
            Erro("latitude", "Latitude fora do intervalo.");
        if (p.Longitude is < -180 or > 180)
            Erro("longitude", "Longitude fora do intervalo.");
        if (p.PrecisaoMetros is < 0)
            Erro("precisaoMetros", "Precisão não pode ser negativa.");
        if (p.LocalDescricao?.Length > Tamanhos.TextoItem)
            Erro("localDescricao", $"Máximo de {Tamanhos.TextoItem} caracteres.");
        if (p.Observacao?.Length > Tamanhos.TextoLongo)
            Erro("observacao", $"Máximo de {Tamanhos.TextoLongo} caracteres.");

        if (p.Itens is null || p.Itens.Count == 0)
            Erro("itens", "A APR precisa ter os itens respondidos.");
        for (var i = 0; i < (p.Itens?.Count ?? 0); i++)
        {
            var item = p.Itens![i];
            if (item.CatalogoItemId is null || item.Ordem is null || item.Texto is null)
                Erro($"itens[{i}]", "Informe o item, a ordem e o texto.");
            if (item.Resposta is null)
                Erro($"itens[{i}].resposta", "Todo item precisa de resposta.");
            if (item.Observacao?.Length > Tamanhos.TextoLongo)
                Erro($"itens[{i}].observacao", $"Máximo de {Tamanhos.TextoLongo} caracteres.");
        }

        if (p.Evidencias?.Count > MaximoEvidencias)
            Erro("evidencias", $"No máximo {MaximoEvidencias} fotos por APR.");
        for (var i = 0; i < (p.Evidencias?.Count ?? 0); i++)
        {
            var e = p.Evidencias![i];
            if (e.Id is null || !HashValido(e.Hash) || e.CapturadaEm is null)
                Erro($"evidencias[{i}]", "Informe o id, o SHA-256 e quando a foto foi tirada.");
            if (e.TamanhoBytes is not (> 0 and <= MaximoBytesFoto) || e.Largura is not > 0 || e.Altura is not > 0)
                Erro($"evidencias[{i}]", $"Tamanho (até {MaximoBytesFoto / 1024 / 1024} MB) e dimensões precisam ser positivos.");
        }
        if (p.Evidencias?.Select(e => e.Id).Distinct().Count() < p.Evidencias?.Count)
            Erro("evidencias", "Foto repetida.");

        return erros.Count > 0 ? Results.ValidationProblem(erros, title: "Dados inválidos.") : null;
    }

    private static bool HashValido(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);

    private static IResult Inconsistente(string titulo) =>
        Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: titulo);

    private static IResult AprNaoEncontrada() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "APR não encontrada.");
}
