using System.Security.Cryptography;
using System.Text;

namespace CampoSeguro.Data.Seguranca;

/// <summary>
/// Tokens de convite e de sessão. Quem recebe o link fica com o token; o banco guarda só o SHA-256,
/// então um vazamento do banco não permite assumir contas nunca ativadas.
/// </summary>
public static class Tokens
{
    /// <summary>256 bits do gerador criptográfico, em Base64 próprio para URL (43 caracteres).</summary>
    public static string Gerar() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>32 bytes, como exigem os CHECKs de <c>convite</c> e <c>sessao_dispositivo</c>.</summary>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
