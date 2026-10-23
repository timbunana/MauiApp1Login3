using CampoSeguro.Data;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Manager.Api;

/// <summary>
/// <b>Único ponto do Painel que desliga o filtro de tenant.</b> Login, aceite de convite e conferência
/// do token acontecem antes de se saber a empresa: a busca é por e-mail ou pelo hash do token, que já
/// identificam uma pessoa só. Toda consulta aqui filtra por uma chave dessas — nunca liste nada.
/// Não chame <c>IgnoreQueryFilters</c> em nenhum outro arquivo deste projeto.
/// </summary>
public sealed class BuscaSemTenant(CampoSeguroDbContext db)
{
    /// <summary>O e-mail é único entre ativos de todas as empresas, então acha no máximo um.</summary>
    public Task<Usuario?> UsuarioAtivoPorEmail(string emailNormalizado, CancellationToken ct) =>
        db.Usuarios.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.EmailNormalizado == emailNormalizado && u.Ativo, ct);

    /// <summary>Confere o dono de um token já assinado: mesmo usuário, mesma empresa, ainda ativo e ainda gestor.</summary>
    public Task<bool> GestorAtivo(Guid usuarioId, Guid tenantId, CancellationToken ct) =>
        db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Id == usuarioId && u.TenantId == tenantId && u.Ativo && u.Papel == Papel.Gestor, ct);

    public Task<Convite?> ConvitePorToken(byte[] tokenHash, CancellationToken ct) =>
        db.Convites.IgnoreQueryFilters().Include(c => c.Usuario).SingleOrDefaultAsync(c => c.TokenHash == tokenHash, ct);

    public Task<string> NomeDaEmpresa(Guid tenantId, CancellationToken ct) =>
        db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Nome).SingleAsync(ct);

    /// <summary>
    /// Marca o convite como usado só se ninguém o usou antes. É o que decide entre duas aberturas
    /// simultâneas do mesmo link: a segunda recebe <c>false</c> e não troca a senha.
    /// </summary>
    public async Task<bool> MarcarConviteUsado(Guid conviteId, DateTimeOffset agora, CancellationToken ct) =>
        await db.Convites.IgnoreQueryFilters()
            .Where(c => c.Id == conviteId && c.UsadoEm == null && c.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsadoEm, agora), ct) == 1;

    /// <summary>Depois de redefinir a senha, os aparelhos conectados precisam entrar de novo.</summary>
    public Task RevogarSessoes(Guid usuarioId, Guid tenantId, DateTimeOffset agora, CancellationToken ct) =>
        db.SessoesDispositivo.IgnoreQueryFilters()
            .Where(s => s.UsuarioId == usuarioId && s.TenantId == tenantId && s.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, agora), ct);
}
