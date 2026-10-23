using CampoSeguro.Data;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Field.Api;

/// <summary>
/// <b>Único ponto do app de campo que desliga o filtro de tenant.</b> Login, renovação e conferência
/// do token acontecem antes de se saber a empresa; a busca é sempre por uma chave que identifica uma
/// pessoa só (e-mail, hash do token, id da sessão). Nunca liste nada por aqui.
/// Não chame <c>IgnoreQueryFilters</c> em nenhum outro arquivo deste projeto.
/// </summary>
public sealed class BuscaSemTenant(CampoSeguroDbContext db)
{
    public Task<Usuario?> UsuarioAtivoPorEmail(string emailNormalizado, CancellationToken ct) =>
        db.Usuarios.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.EmailNormalizado == emailNormalizado && u.Ativo, ct);

    public Task<SessaoDispositivo?> SessaoPorToken(byte[] tokenHash, CancellationToken ct) =>
        db.SessoesDispositivo.IgnoreQueryFilters().Include(s => s.Usuario).SingleOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

    /// <summary>
    /// Confere, a cada requisição, que a sessão do token ainda vale e é do mesmo técnico e empresa.
    /// É o que faz desativar o colaborador ou redefinir a senha cortarem o acesso na hora.
    /// </summary>
    public Task<bool> SessaoAtiva(Guid sessaoId, Guid usuarioId, Guid tenantId, DateTimeOffset agora, CancellationToken ct) =>
        db.SessoesDispositivo.IgnoreQueryFilters().AnyAsync(s =>
            s.Id == sessaoId && s.UsuarioId == usuarioId && s.TenantId == tenantId
            && s.RevogadaEm == null && s.ExpiraEm > agora
            && s.Usuario!.Ativo && s.Usuario.Papel == Papel.Tecnico, ct);

    public Task<string> NomeDaEmpresa(Guid tenantId, CancellationToken ct) =>
        db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Nome).SingleAsync(ct);
}
