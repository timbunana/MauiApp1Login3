namespace CampoSeguro.Platform.Api.Empresas;

/// <summary>Seção <c>Convites</c> da configuração.</summary>
public sealed class OpcoesConvite
{
    /// <summary>Página do Painel onde o gestor define a senha, ex.: <c>https://gestao.camposeguro.com.br/convite</c>.</summary>
    public string UrlAceite { get; set; } = "";

    /// <summary>
    /// O token vai no fragmento (<c>#</c>), que o navegador não envia ao servidor: ele não aparece em
    /// log de acesso do Nginx nem no cabeçalho Referer. A página lê o fragmento e envia o token por POST.
    /// </summary>
    public string Link(string token) => $"{UrlAceite}#{token}";

    public void Validar()
    {
        if (!Uri.TryCreate(UrlAceite, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.Fragment != "")
            throw new InvalidOperationException("Defina Convites__UrlAceite com a URL absoluta da página de aceite, sem '#'.");
    }
}
