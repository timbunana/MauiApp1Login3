using CampoSeguro.Data;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Platform.Api;

/// <summary>
/// <b>Único ponto da Plataforma que desliga o filtro de tenant.</b> A Plataforma roda sem tenant no
/// contexto, então qualquer consulta a uma entidade de tenant fora daqui não devolve nada — o que é
/// o comportamento seguro. Não chame <c>IgnoreQueryFilters</c> em nenhum outro arquivo deste projeto,
/// e nunca copie este arquivo para outro backend.
/// </summary>
internal static class TodosOsTenants
{
    public static IQueryable<T> DeTodosOsTenants<T>(this CampoSeguroDbContext db) where T : class, IPertenceAoTenant =>
        db.Set<T>().IgnoreQueryFilters();
}
