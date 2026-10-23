using Microsoft.AspNetCore.Identity;

namespace CampoSeguro.Data.Seguranca;

/// <summary>
/// Hash de senha de administradores e usuários. Usa o PBKDF2 do <see cref="PasswordHasher{TUser}"/>
/// (formato V3: HMAC-SHA512, sal aleatório, iterações gravadas no próprio hash). O formato é
/// versionado, então trocar de algoritmo depois não invalida as senhas existentes: o login
/// confere o hash antigo e regrava no novo quando <see cref="ResultadoSenha.PrecisaRegravar"/>.
/// </summary>
public static class Senhas
{
    public const int TamanhoMinimo = 12;

    /// <summary>Limite para que uma senha gigante não vire negação de serviço no PBKDF2.</summary>
    public const int TamanhoMaximo = 128;

    private static readonly PasswordHasher<object> Hasher = new();

    /// <summary>
    /// Conferido quando o e-mail não existe, para que a resposta demore o mesmo que uma senha errada
    /// e o tempo não revele quais e-mails estão cadastrados.
    /// </summary>
    private static readonly string HashFalso = Hasher.HashPassword(new object(), Guid.NewGuid().ToString());

    public static string? Problema(string senha) => senha.Length switch
    {
        < TamanhoMinimo => $"A senha precisa ter pelo menos {TamanhoMinimo} caracteres.",
        > TamanhoMaximo => $"A senha pode ter no máximo {TamanhoMaximo} caracteres.",
        _ when string.IsNullOrWhiteSpace(senha) => "A senha não pode ser só espaços.",
        _ => null,
    };

    public static string Gerar(string senha) => Hasher.HashPassword(new object(), senha);

    /// <summary>Com <paramref name="hash"/> nulo, gasta o mesmo tempo e devolve <see cref="ResultadoSenha.Invalida"/>.</summary>
    public static ResultadoSenha Conferir(string? hash, string senha)
    {
        if (senha.Length > TamanhoMaximo)
            return ResultadoSenha.Invalida;

        var resultado = Hasher.VerifyHashedPassword(new object(), hash ?? HashFalso, senha);
        return hash is null ? ResultadoSenha.Invalida : resultado switch
        {
            PasswordVerificationResult.Success => ResultadoSenha.Valida,
            PasswordVerificationResult.SuccessRehashNeeded => ResultadoSenha.PrecisaRegravar,
            _ => ResultadoSenha.Invalida,
        };
    }
}

public enum ResultadoSenha
{
    Invalida,
    Valida,

    /// <summary>Senha certa, mas o hash está num formato antigo: grave <see cref="Senhas.Gerar"/> de novo.</summary>
    PrecisaRegravar,
}
