using CampoSeguro.Data;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Manager.Api.Perfil;

public static class PerfilEndpoints
{
    public sealed record Empresa(Guid Id, string Nome);

    public sealed record MeuPerfil(Guid Id, string Nome, string Email, Papel Papel, Empresa Empresa);

    public static void MapPerfil(this IEndpointRouteBuilder app) =>
        app.MapGet("/eu", Eu).WithTags("Perfil");

    /// <summary>
    /// Lê pelo filtro de tenant normal, sem atalho: se o tenant do token não chegasse ao contexto,
    /// esta rota devolveria 404 em vez dos dados — é a prova mais simples de que o isolamento funciona.
    /// </summary>
    private static async Task<IResult> Eu(CampoSeguroDbContext db, IContextoTenant contexto, CancellationToken ct)
    {
        var usuario = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.Id == contexto.UsuarioId, ct);
        if (usuario is null)
            return Results.NotFound();

        var empresa = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == usuario.TenantId, ct);
        return Results.Ok(new MeuPerfil(usuario.Id, usuario.Nome, usuario.Email, usuario.Papel, new Empresa(empresa.Id, empresa.Nome)));
    }
}
