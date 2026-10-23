namespace CampoSeguro.Data.Configuracoes;

/// <summary>Limites das colunas de texto. Públicos para que as APIs validem com os mesmos números.</summary>
public static class Tamanhos
{
    public const int Nome = 200;

    /// <summary>Limite do RFC 5321 para um endereço completo.</summary>
    public const int Email = 320;

    /// <summary>Hash no formato texto do algoritmo (ex.: PBKDF2 ou Argon2 com sal e parâmetros).</summary>
    public const int SenhaHash = 200;

    public const int TextoItem = 500;
    public const int TextoLongo = 2000;
    public const int Caminho = 500;
}
