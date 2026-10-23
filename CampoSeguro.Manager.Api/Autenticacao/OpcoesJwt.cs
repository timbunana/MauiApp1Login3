using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CampoSeguro.Manager.Api.Autenticacao;

/// <summary>
/// Seção <c>Jwt</c> da configuração. Emissor, audiência e chave são <b>diferentes dos da Plataforma</b>:
/// um token do Painel nunca abre a Plataforma, nem o contrário. A chave vem de <c>Jwt__Chave</c>.
/// </summary>
public sealed class OpcoesJwt
{
    public string Emissor { get; set; } = "camposeguro-gestao";
    public string Audiencia { get; set; } = "camposeguro-gestao";
    public string Chave { get; set; } = "";

    /// <summary>Uma jornada de trabalho. Desativar o gestor corta o acesso antes disso.</summary>
    public TimeSpan Validade { get; set; } = TimeSpan.FromHours(8);

    public SymmetricSecurityKey ChaveAssinatura() => new(Encoding.UTF8.GetBytes(Chave));

    public void Validar()
    {
        if (Encoding.UTF8.GetByteCount(Chave) < 32)
            throw new InvalidOperationException("Defina Jwt__Chave com pelo menos 32 bytes aleatórios.");
        if (Validade <= TimeSpan.Zero)
            throw new InvalidOperationException("Jwt:Validade precisa ser positiva.");
    }
}

/// <summary>Claims próprias do Painel, além das registradas (sub, name, email).</summary>
public static class Claims
{
    /// <summary>Empresa do usuário. É daqui, e só daqui, que sai o filtro de tenant.</summary>
    public const string Tenant = "tenant";

    public const string Papel = "papel";
}
