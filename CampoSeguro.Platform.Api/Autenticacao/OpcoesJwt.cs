using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CampoSeguro.Platform.Api.Autenticacao;

/// <summary>Seção <c>Jwt</c> da configuração. A chave vem de variável de ambiente (<c>Jwt__Chave</c>), nunca do appsettings.</summary>
public sealed class OpcoesJwt
{
    public string Emissor { get; set; } = "camposeguro-plataforma";
    public string Audiencia { get; set; } = "camposeguro-plataforma";
    public string Chave { get; set; } = "";

    /// <summary>Uma jornada de trabalho. Desativar o administrador corta o acesso antes disso (ver <see cref="AutenticacaoPlataforma"/>).</summary>
    public TimeSpan Validade { get; set; } = TimeSpan.FromHours(8);

    public SymmetricSecurityKey ChaveAssinatura() => new(Encoding.UTF8.GetBytes(Chave));

    public void Validar()
    {
        // HS256 com chave menor que o próprio hash enfraquece a assinatura; a biblioteca também recusa.
        if (Encoding.UTF8.GetByteCount(Chave) < 32)
            throw new InvalidOperationException("Defina Jwt__Chave com pelo menos 32 bytes aleatórios.");
        if (Validade <= TimeSpan.Zero)
            throw new InvalidOperationException("Jwt:Validade precisa ser positiva.");
    }
}
