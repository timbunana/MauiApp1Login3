using System.Net.Mail;

namespace CampoSeguro.Domain;

public static class Emails
{
    /// <summary>Forma usada no login e na unicidade: sem espaços nas pontas e em minúsculas.</summary>
    public static string Normalizar(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Endereço simples (<c>nome@dominio</c>), sem nome de exibição nem lista.</summary>
    public static bool Valido(string email)
    {
        var limpo = email.Trim();
        return limpo.Length <= 320
            && MailAddress.TryCreate(limpo, out var endereco)
            && endereco.Address == limpo
            && endereco.Host.Contains('.');
    }
}

public static class Cnpj
{
    /// <summary>Remove a máscara. O banco guarda só os 14 dígitos.</summary>
    public static string Normalizar(string cnpj) => new(cnpj.Where(char.IsAsciiDigit).ToArray());

    /// <summary>
    /// Confere os dois dígitos verificadores de um CNPJ já normalizado. Recusa sequências repetidas
    /// (00000000000000, 11111111111111…), que passam no cálculo mas não existem.
    /// </summary>
    public static bool Valido(string digitos)
    {
        if (digitos.Length != 14 || !digitos.All(char.IsAsciiDigit) || digitos.Distinct().Count() == 1)
            return false;

        return digitos[12] - '0' == Verificador(digitos[..12]) && digitos[13] - '0' == Verificador(digitos[..13]);
    }

    private static int Verificador(string prefixo)
    {
        // Pesos de 2 a 9, da direita para a esquerda, recomeçando em 2.
        var soma = 0;
        for (var i = 0; i < prefixo.Length; i++)
            soma += (prefixo[prefixo.Length - 1 - i] - '0') * (2 + i % 8);

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
