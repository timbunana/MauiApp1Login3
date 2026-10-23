using CampoSeguro.Data;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Field.Api.Catalogos;

/// <summary>
/// O que o app baixa e guarda no aparelho: a última versão publicada de cada catálogo ativo da
/// empresa. Rascunhos nunca saem daqui. O app grava o <c>VersaoId</c> em cada APR.
/// </summary>
public static class CatalogosEndpoints
{
    public sealed record Item(Guid ItemId, int Ordem, string Texto);

    public sealed record CatalogoPublicado(
        Guid CatalogoId, string Nome, string? Descricao, Guid VersaoId, int Numero, DateTimeOffset PublicadaEm, IReadOnlyList<Item> Itens);

    public static void MapCatalogos(this IEndpointRouteBuilder app) =>
        app.MapGet("/catalogos", Listar).WithTags("Catálogos");

    private static async Task<IResult> Listar(CampoSeguroDbContext db, CancellationToken ct)
    {
        var versoes = await db.CatalogoVersoes.AsNoTracking()
            .Include(v => v.Catalogo)
            .Include(v => v.Itens)
            .Where(v => v.PublicadaEm != null && v.Catalogo!.Ativo
                && !db.CatalogoVersoes.Any(o => o.CatalogoId == v.CatalogoId && o.PublicadaEm != null && o.Numero > v.Numero))
            .ToListAsync(ct);

        return Results.Ok(versoes
            .OrderBy(v => v.Catalogo!.Nome)
            .Select(v => new CatalogoPublicado(
                v.CatalogoId, v.Catalogo!.Nome, v.Catalogo.Descricao, v.Id, v.Numero, v.PublicadaEm!.Value,
                v.Itens.OrderBy(i => i.Ordem).Select(i => new Item(i.CatalogoItemId, i.Ordem, i.Texto)).ToList())));
    }
}
