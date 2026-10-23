using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CampoSeguro.Field.Api.Autenticacao;

/// <summary>
/// Seção <c>Jwt</c>. Emissor, audiência e chave próprios do app de campo: um token daqui não abre o
/// Painel nem a Plataforma. A chave vem de <c>Jwt__Chave</c>.
/// </summary>
public sealed class OpcoesJwt
{
    public string Emissor { get; set; } = "camposeguro-campo";
    public string Audiencia { get; set; } = "camposeguro-campo";
    public string Chave { get; set; } = "";

    /// <summary>
    /// Curta, porque quem dura é a sessão do aparelho: o app renova sozinho ao sincronizar. Expirar
    /// sem sinal não impede o técnico de registrar APR, que é gravada no aparelho.
    /// </summary>
    public TimeSpan Validade { get; set; } = TimeSpan.FromHours(1);

    public SymmetricSecurityKey ChaveAssinatura() => new(Encoding.UTF8.GetBytes(Chave));

    public void Validar()
    {
        if (Encoding.UTF8.GetByteCount(Chave) < 32)
            throw new InvalidOperationException("Defina Jwt__Chave com pelo menos 32 bytes aleatórios.");
        if (Validade <= TimeSpan.Zero)
            throw new InvalidOperationException("Jwt:Validade precisa ser positiva.");
    }
}

/// <summary>Claims próprias do app de campo, além das registradas (sub, name).</summary>
public static class Claims
{
    /// <summary>Empresa do técnico. É daqui, e só daqui, que sai o filtro de tenant.</summary>
    public const string Tenant = "tenant";

    /// <summary>Sessão do aparelho: a APR recebida registra por qual aparelho chegou.</summary>
    public const string Sessao = "sid";

    public const string Papel = "papel";
}
