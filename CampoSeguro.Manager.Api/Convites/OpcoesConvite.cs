namespace CampoSeguro.Manager.Api.Convites;

/// <summary>Seção <c>Convites</c> da configuração.</summary>
public sealed class OpcoesConvite
{
    /// <summary>Página de aceite do Painel, ex.: <c>https://gestao.camposeguro.com.br/convite</c>. A mesma usada pela Plataforma.</summary>
    public string UrlAceite { get; set; } = "";

    /// <summary>Token no fragmento (<c>#</c>): o navegador não o envia ao servidor, então ele não vai para log nem Referer.</summary>
    public string Link(string token) => $"{UrlAceite}#{token}";

    public void Validar()
    {
        if (!Uri.TryCreate(UrlAceite, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.Fragment != "")
            throw new InvalidOperationException("Defina Convites__UrlAceite com a URL absoluta da página de aceite, sem '#'.");
    }
}
