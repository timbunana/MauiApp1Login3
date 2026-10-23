using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Data.Seguranca;

/// <summary>Emissão de links de convite e de redefinição de senha, igual em todos os backends.</summary>
public static class LinksDeAcesso
{
    /// <summary>O token em claro só existe no retorno: vai no link e nunca é gravado.</summary>
    public static (Convite Convite, string Token) Novo(Usuario usuario, TipoConvite tipo)
    {
        var token = Tokens.Gerar();
        return (new Convite
        {
            TenantId = usuario.TenantId,
            UsuarioId = usuario.Id,
            Tipo = tipo,
            TokenHash = Tokens.Hash(token),
            ExpiraEm = DateTimeOffset.UtcNow + Convite.Validade,
        }, token);
    }

    /// <summary>
    /// Revoga os links pendentes do mesmo tipo e emite outro. O banco aceita um só pendente por
    /// usuário e tipo, então a revogação é gravada antes da inclusão, na mesma transação.
    /// </summary>
    /// <param name="doUsuario">
    /// Convites já restritos ao usuário. O chamador decide se a consulta passa pelo filtro de tenant
    /// (Painel) ou não (Plataforma).
    /// </param>
    /// <exception cref="DbUpdateException">Outro link foi emitido ao mesmo tempo; ver <see cref="ErrosDoBanco.ViolouUnicidade"/>.</exception>
    public static async Task<(Convite Convite, string Token)> Reemitir(
        CampoSeguroDbContext db, IQueryable<Convite> doUsuario, Usuario usuario, TipoConvite tipo, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var agora = DateTimeOffset.UtcNow;
        foreach (var anterior in await doUsuario.Where(c => c.Tipo == tipo && c.UsadoEm == null && c.RevogadoEm == null).ToListAsync(ct))
            anterior.RevogadoEm = agora;
        await db.SaveChangesAsync(ct);

        var (convite, token) = Novo(usuario, tipo);
        db.Convites.Add(convite);
        await db.SaveChangesAsync(ct);

        await transacao.CommitAsync(ct);
        return (convite, token);
    }
}
