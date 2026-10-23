using CampoSeguro.Domain;

namespace CampoSeguro.Testes.Infra;

/// <summary>Dados únicos por teste, para que os testes não colidam no banco compartilhado.</summary>
public static class Gerar
{
    /// <summary>12 dígitos aleatórios e o primeiro par verificador que <see cref="Domain.Cnpj.Valido"/> aceita.</summary>
    public static string Cnpj()
    {
        var prefixo = string.Concat(Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)));
        return Enumerable.Range(0, 100).Select(dv => $"{prefixo}{dv:00}").First(Domain.Cnpj.Valido);
    }

    public static string Email(string nome = "pessoa") => $"{nome}.{Guid.NewGuid():N}@teste.com.br";

    /// <summary>Hash de 32 bytes no formato literal do PostgreSQL.</summary>
    public static string Hash(byte valor) => $"decode(repeat('{valor:x2}', 32), 'hex')";
}
