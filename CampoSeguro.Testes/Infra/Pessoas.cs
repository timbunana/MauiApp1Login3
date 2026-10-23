using CampoSeguro.Data.Seguranca;
using CampoSeguro.Domain;

namespace CampoSeguro.Testes.Infra;

/// <summary>Empresas, colaboradores e convites gravados direto no banco, sem passar por API.</summary>
public static class Pessoas
{
    public const string Senha = "senha-do-colaborador-1";

    public static async Task<Guid> NovaEmpresa(BancoDeTeste banco, string nome = "Empresa de Teste")
    {
        banco.Exigir();
        var empresa = new Tenant { Nome = nome, Documento = Gerar.Cnpj() };
        await using var db = banco.Contexto(null);
        db.Tenants.Add(empresa);
        await db.SaveChangesAsync();
        return empresa.Id;
    }

    /// <summary>Com <paramref name="comSenha"/> falso, o colaborador ainda não aceitou o convite.</summary>
    public static async Task<Usuario> NovoUsuario(BancoDeTeste banco, Guid tenant, Papel papel, bool comSenha = true)
    {
        var email = Gerar.Email(papel.ToString().ToLowerInvariant());
        var usuario = new Usuario
        {
            TenantId = tenant,
            Nome = $"{papel} de Teste",
            Email = email,
            EmailNormalizado = Emails.Normalizar(email),
            Papel = papel,
            SenhaHash = comSenha ? Senhas.Gerar(Senha) : null,
        };

        await using var db = banco.Contexto(null);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    /// <summary>Devolve o token em claro, como sairia no link.</summary>
    public static async Task<string> NovoConvite(BancoDeTeste banco, Usuario usuario, TipoConvite tipo = TipoConvite.Convite)
    {
        var token = Tokens.Gerar();
        await using var db = banco.Contexto(null);
        db.Convites.Add(new Convite
        {
            TenantId = usuario.TenantId,
            UsuarioId = usuario.Id,
            Tipo = tipo,
            TokenHash = Tokens.Hash(token),
            ExpiraEm = DateTimeOffset.UtcNow + Convite.Validade,
        });
        await db.SaveChangesAsync();
        return token;
    }
}
