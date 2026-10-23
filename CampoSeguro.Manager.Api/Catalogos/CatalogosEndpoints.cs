using CampoSeguro.Data;
using CampoSeguro.Data.Configuracoes;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Manager.Api.Catalogos;

/// <summary>
/// Checklists da empresa. Cada catálogo tem no máximo um rascunho editável; publicar congela a versão
/// (o banco recusa qualquer alteração depois, por trigger) e é ela que o app baixa e que as APRs citam.
/// A próxima edição abre a versão N+1 a partir da última publicada.
/// </summary>
public static class CatalogosEndpoints
{
    public const int MaximoItens = 100;

    public static void MapCatalogos(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/catalogos").WithTags("Catálogos");

        grupo.MapGet("/", Listar);
        grupo.MapPost("/", Criar);
        grupo.MapGet("/modelos", (Func<IEnumerable<ResumoModelo>>)(() =>
            Modelos.Todos.Select(m => new ResumoModelo(m.Codigo, m.Nome, m.Descricao, m.Itens.Count))));
        grupo.MapPost("/modelos/{codigo}", Importar);

        grupo.MapGet("/{id:guid}", Detalhar);
        grupo.MapPut("/{id:guid}", Alterar);
        grupo.MapPost("/{id:guid}/desativar", (Guid id, CampoSeguroDbContext db, CancellationToken ct) => MudarAtivo(id, false, db, ct));
        grupo.MapPost("/{id:guid}/reativar", (Guid id, CampoSeguroDbContext db, CancellationToken ct) => MudarAtivo(id, true, db, ct));
        grupo.MapGet("/{id:guid}/versoes/{numero:int}", LerVersao);

        grupo.MapGet("/{id:guid}/rascunho", LerRascunho);
        grupo.MapPost("/{id:guid}/rascunho", AbrirRascunho);
        grupo.MapPut("/{id:guid}/rascunho", EditarRascunho);
        grupo.MapDelete("/{id:guid}/rascunho", DescartarRascunho);
        grupo.MapPost("/{id:guid}/rascunho/publicar", Publicar);
    }

    // --- Catálogo

    /// <summary>Por padrão só os ativos: catálogo desativado some do app, mas suas versões continuam citadas pelas APRs.</summary>
    private static async Task<IResult> Listar(bool? incluirInativos, CampoSeguroDbContext db, CancellationToken ct)
    {
        var consulta = db.Catalogos.AsNoTracking();
        if (incluirInativos != true)
            consulta = consulta.Where(c => c.Ativo);

        var catalogos = await consulta.OrderBy(c => c.Nome).ToListAsync(ct);
        var ids = catalogos.Select(c => c.Id).ToList();
        var versoes = await Versoes(db.CatalogoVersoes.Where(v => ids.Contains(v.CatalogoId))).ToListAsync(ct);

        return Results.Ok(catalogos.Select(c =>
        {
            var doCatalogo = versoes.Where(v => v.CatalogoId == c.Id).ToList();
            return new ResumoCatalogo(c.Id, c.Nome, c.Descricao, c.Ativo,
                doCatalogo.Where(v => v.Resumo.PublicadaEm != null).MaxBy(v => v.Resumo.Numero)?.Resumo,
                doCatalogo.SingleOrDefault(v => v.Resumo.PublicadaEm == null)?.Resumo);
        }));
    }

    /// <summary>Catálogo vazio, já com o rascunho da versão 1 aberto.</summary>
    private static async Task<IResult> Criar(DadosCatalogo pedido, CampoSeguroDbContext db, CancellationToken ct)
    {
        if (Validar(pedido) is { } invalido)
            return invalido;

        return await Gravar(db, NovoCatalogo(db, pedido.Nome!.Trim(), Limpar(pedido.Descricao), []), ct);
    }

    private static async Task<IResult> Importar(string codigo, CampoSeguroDbContext db, CancellationToken ct)
    {
        if (Modelos.PorCodigo(codigo) is not { } modelo)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Modelo não encontrado.");

        return await Gravar(db, NovoCatalogo(db, modelo.Nome, modelo.Descricao, modelo.Itens), ct);
    }

    private static async Task<IResult> Detalhar(Guid id, CampoSeguroDbContext db, CancellationToken ct)
    {
        var catalogo = await db.Catalogos.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        return catalogo is null ? CatalogoNaoEncontrado() : Results.Ok(await Detalhe(db, catalogo, ct));
    }

    /// <summary>Nome e descrição. Não muda o conteúdo de nenhuma versão: isso só pelo rascunho.</summary>
    private static async Task<IResult> Alterar(Guid id, DadosCatalogo pedido, CampoSeguroDbContext db, CancellationToken ct)
    {
        if (Validar(pedido) is { } invalido)
            return invalido;

        var catalogo = await db.Catalogos.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (catalogo is null)
            return CatalogoNaoEncontrado();

        catalogo.Nome = pedido.Nome!.Trim();
        catalogo.Descricao = Limpar(pedido.Descricao);
        return await Gravar(db, catalogo, ct, criado: false);
    }

    private static async Task<IResult> MudarAtivo(Guid id, bool ativo, CampoSeguroDbContext db, CancellationToken ct)
    {
        var catalogo = await db.Catalogos.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (catalogo is null)
            return CatalogoNaoEncontrado();

        catalogo.Ativo = ativo;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Detalhe(db, catalogo, ct));
    }

    private static async Task<IResult> LerVersao(Guid id, int numero, CampoSeguroDbContext db, CancellationToken ct)
    {
        var versao = await db.CatalogoVersoes.AsNoTracking().Include(v => v.Itens)
            .SingleOrDefaultAsync(v => v.CatalogoId == id && v.Numero == numero, ct);
        return versao is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Versão não encontrada.")
            : Results.Ok(Conteudo(versao));
    }

    // --- Rascunho

    private static async Task<IResult> LerRascunho(Guid id, CampoSeguroDbContext db, CancellationToken ct)
    {
        var rascunho = await Rascunho(db, id).AsNoTracking().SingleOrDefaultAsync(ct);
        if (rascunho is not null)
            return Results.Ok(Conteudo(rascunho));

        return await db.Catalogos.AnyAsync(c => c.Id == id, ct) ? SemRascunho() : CatalogoNaoEncontrado();
    }

    /// <summary>Versão N+1, começando com os itens da última publicada (mesma identidade, mesmo texto, mesma ordem).</summary>
    private static async Task<IResult> AbrirRascunho(Guid id, CampoSeguroDbContext db, CancellationToken ct)
    {
        if (!await db.Catalogos.AnyAsync(c => c.Id == id, ct))
            return CatalogoNaoEncontrado();
        if (await Rascunho(db, id).AnyAsync(ct))
            return Conflito("Este catálogo já tem um rascunho aberto.");

        var anteriores = db.CatalogoVersoes.Where(v => v.CatalogoId == id);
        var ultimaPublicada = await anteriores.Include(v => v.Itens)
            .Where(v => v.PublicadaEm != null).OrderByDescending(v => v.Numero).FirstOrDefaultAsync(ct);

        var rascunho = new CatalogoVersao
        {
            CatalogoId = id,
            Numero = (await anteriores.MaxAsync(v => (int?)v.Numero, ct) ?? 0) + 1,
            Itens = ultimaPublicada?.Itens
                .Select(i => new CatalogoVersaoItem { CatalogoItemId = i.CatalogoItemId, Ordem = i.Ordem, Texto = i.Texto })
                .ToList() ?? [],
        };
        db.CatalogoVersoes.Add(rascunho);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.ViolouUnicidade())
        {
            return Conflito("Outro rascunho foi aberto ao mesmo tempo.");
        }

        return Results.Created($"/catalogos/{id}/rascunho", Conteudo(rascunho));
    }

    /// <summary>
    /// Substitui a lista inteira. Itens com <c>itemId</c> precisam ser deste catálogo — é aqui, e não no
    /// banco, que se garante que uma versão não mistura itens de catálogos diferentes (BANCO.md).
    /// </summary>
    private static async Task<IResult> EditarRascunho(Guid id, EdicaoRascunho pedido, CampoSeguroDbContext db, CancellationToken ct)
    {
        var rascunho = await Rascunho(db, id).SingleOrDefaultAsync(ct);
        if (rascunho is null)
            return await db.Catalogos.AnyAsync(c => c.Id == id, ct) ? SemRascunho() : CatalogoNaoEncontrado();

        var itens = pedido.Itens ?? [];
        var erros = new Dictionary<string, string[]>();
        if (itens.Count > MaximoItens)
            erros["itens"] = [$"Um catálogo pode ter no máximo {MaximoItens} itens."];

        var textosVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < itens.Count; i++)
        {
            var texto = itens[i].Texto?.Trim() ?? "";
            if (texto.Length == 0)
                erros[$"itens[{i}].texto"] = ["Informe o texto do item."];
            else if (texto.Length > Tamanhos.TextoItem)
                erros[$"itens[{i}].texto"] = [$"O texto pode ter no máximo {Tamanhos.TextoItem} caracteres."];
            else if (!textosVistos.Add(texto))
                erros[$"itens[{i}].texto"] = ["Item repetido."];
        }

        var idsInformados = itens.Where(i => i.ItemId is not null).Select(i => i.ItemId!.Value).ToList();
        var idsDoCatalogo = await db.CatalogoItens
            .Where(i => i.CatalogoId == id && idsInformados.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
        var idsVistos = new HashSet<Guid>();
        for (var i = 0; i < itens.Count; i++)
        {
            if (itens[i].ItemId is not { } itemId)
                continue;
            if (!idsDoCatalogo.Contains(itemId))
                erros[$"itens[{i}].itemId"] = ["Este item não pertence a este catálogo."];
            else if (!idsVistos.Add(itemId))
                erros[$"itens[{i}].itemId"] = ["Item repetido."];
        }

        if (erros.Count > 0)
            return Results.ValidationProblem(erros, title: "Dados inválidos.");

        // Apagar e incluir em gravações separadas: os índices únicos (versão, ordem) e (versão, item)
        // recusariam a lista nova se ela chegasse antes de a antiga sair.
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        await db.CatalogoVersaoItens.Where(i => i.CatalogoVersaoId == rascunho.Id).ExecuteDeleteAsync(ct);

        var novos = itens.Select((item, i) => new CatalogoVersaoItem
        {
            CatalogoVersaoId = rascunho.Id,
            CatalogoItemId = item.ItemId ?? NovoItem(db, id).Id,
            Ordem = i + 1,
            Texto = item.Texto!.Trim(),
        }).ToList();
        db.CatalogoVersaoItens.AddRange(novos);
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        rascunho.Itens = novos;
        return Results.Ok(Conteudo(rascunho));
    }

    private static async Task<IResult> DescartarRascunho(Guid id, CampoSeguroDbContext db, CancellationToken ct)
    {
        var rascunho = await Rascunho(db, id).SingleOrDefaultAsync(ct);
        if (rascunho is null)
            return await db.Catalogos.AnyAsync(c => c.Id == id, ct) ? SemRascunho() : CatalogoNaoEncontrado();

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        await db.CatalogoVersaoItens.Where(i => i.CatalogoVersaoId == rascunho.Id).ExecuteDeleteAsync(ct);
        await db.CatalogoVersoes.Where(v => v.Id == rascunho.Id).ExecuteDeleteAsync(ct);
        await transacao.CommitAsync(ct);

        return Results.NoContent();
    }

    /// <summary>Daqui em diante a versão é imutável e passa a ser a que o app baixa.</summary>
    private static async Task<IResult> Publicar(Guid id, CampoSeguroDbContext db, ILoggerFactory logs, IContextoTenant contexto, CancellationToken ct)
    {
        var rascunho = await Rascunho(db, id).SingleOrDefaultAsync(ct);
        if (rascunho is null)
            return await db.Catalogos.AnyAsync(c => c.Id == id, ct) ? SemRascunho() : CatalogoNaoEncontrado();
        if (rascunho.Itens.Count == 0)
            return Conflito("Inclua pelo menos um item antes de publicar.");

        rascunho.PublicadaEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logs.CreateLogger(nameof(CatalogosEndpoints)).LogInformation(
            "Gestor {GestorId} publicou a versão {Numero} do catálogo {CatalogoId}", contexto.UsuarioId, rascunho.Numero, id);

        return Results.Ok(Conteudo(rascunho));
    }

    // --- Apoio

    private static IQueryable<CatalogoVersao> Rascunho(CampoSeguroDbContext db, Guid catalogoId) =>
        db.CatalogoVersoes.Include(v => v.Itens).Where(v => v.CatalogoId == catalogoId && v.PublicadaEm == null);

    private sealed record LinhaVersao(Guid CatalogoId, ResumoVersao Resumo);

    private static IQueryable<LinhaVersao> Versoes(IQueryable<CatalogoVersao> versoes) =>
        versoes.OrderBy(v => v.Numero)
            .Select(v => new LinhaVersao(v.CatalogoId, new ResumoVersao(v.Numero, v.PublicadaEm, v.Itens.Count)));

    private static Catalogo NovoCatalogo(CampoSeguroDbContext db, string nome, string? descricao, IReadOnlyList<string> textos)
    {
        var catalogo = new Catalogo { Nome = nome, Descricao = descricao };
        db.Catalogos.Add(catalogo);
        db.CatalogoVersoes.Add(new CatalogoVersao
        {
            CatalogoId = catalogo.Id,
            Numero = 1,
            Itens = textos.Select((texto, i) => new CatalogoVersaoItem
            {
                CatalogoItemId = NovoItem(db, catalogo.Id).Id,
                Ordem = i + 1,
                Texto = texto,
            }).ToList(),
        });
        return catalogo;
    }

    private static CatalogoItem NovoItem(CampoSeguroDbContext db, Guid catalogoId)
    {
        var item = new CatalogoItem { CatalogoId = catalogoId };
        db.CatalogoItens.Add(item);
        return item;
    }

    /// <summary>O nome é único por empresa; o índice do banco decide quando duas gravações correm juntas.</summary>
    private static async Task<IResult> Gravar(CampoSeguroDbContext db, Catalogo catalogo, CancellationToken ct, bool criado = true)
    {
        if (await db.Catalogos.AnyAsync(c => c.Nome == catalogo.Nome && c.Id != catalogo.Id, ct))
            return NomeEmUso();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.ViolouUnicidade())
        {
            return NomeEmUso();
        }

        var detalhe = await Detalhe(db, catalogo, ct);
        return criado ? Results.Created($"/catalogos/{catalogo.Id}", detalhe) : Results.Ok(detalhe);
    }

    private static async Task<DetalheCatalogo> Detalhe(CampoSeguroDbContext db, Catalogo c, CancellationToken ct)
    {
        var versoes = await Versoes(db.CatalogoVersoes.Where(v => v.CatalogoId == c.Id)).ToListAsync(ct);
        return new DetalheCatalogo(c.Id, c.Nome, c.Descricao, c.Ativo, versoes.Select(v => v.Resumo).ToList());
    }

    private static Versao Conteudo(CatalogoVersao v) =>
        new(v.Numero, v.PublicadaEm, v.Itens.OrderBy(i => i.Ordem).Select(i => new ItemVersao(i.CatalogoItemId, i.Ordem, i.Texto)).ToList());

    private static IResult? Validar(DadosCatalogo pedido)
    {
        var erros = new Dictionary<string, string[]>();
        var nome = pedido.Nome?.Trim() ?? "";
        if (nome.Length == 0)
            erros["nome"] = ["Informe o nome do catálogo."];
        else if (nome.Length > Tamanhos.Nome)
            erros["nome"] = [$"O nome pode ter no máximo {Tamanhos.Nome} caracteres."];
        if ((pedido.Descricao?.Trim().Length ?? 0) > Tamanhos.TextoLongo)
            erros["descricao"] = [$"A descrição pode ter no máximo {Tamanhos.TextoLongo} caracteres."];

        return erros.Count > 0 ? Results.ValidationProblem(erros, title: "Dados inválidos.") : null;
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static IResult CatalogoNaoEncontrado() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Catálogo não encontrado.");

    private static IResult SemRascunho() =>
        Conflito("Este catálogo não tem rascunho aberto. Abra a próxima versão para editar.");

    private static IResult NomeEmUso() => Conflito("Já existe um catálogo com este nome.");

    private static IResult Conflito(string titulo) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: titulo);
}
