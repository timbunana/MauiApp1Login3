using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CampoSeguro.Data;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CampoSeguro.Testes.Gestao;

/// <summary>
/// A Manager.Api inteira em memória. A chave JWT é diferente da usada em <see cref="PlataformaApi"/>.
/// Para ler o que o app de campo gravou, passe a mesma pasta de arquivos da <c>CampoApi</c>.
/// </summary>
public sealed class GestaoApi(BancoDeTeste banco, string? raizArquivos = null) : WebApplicationFactory<CampoSeguro.Manager.Api.Autenticacao.OpcoesJwt>
{
    private readonly string raiz = raizArquivos ?? Path.Combine(Path.GetTempPath(), "camposeguro-testes", Guid.NewGuid().ToString("N"));

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (raizArquivos is null && Directory.Exists(raiz))
            Directory.Delete(raiz, recursive: true);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting(CampoSeguroDb.VariavelConexao, banco.Conexao);
        builder.UseSetting("Armazenamento:Raiz", raiz);
        builder.UseSetting("Jwt:Chave", "outra-chave-de-teste-so-do-painel-0123456789");
        builder.UseSetting("Convites:UrlAceite", PlataformaApi.UrlAceite);
        builder.UseSetting("Login:TentativasPorMinuto", "1000");
    }

    /// <summary>Empresa nova com um gestor já logado.</summary>
    public async Task<(Guid Empresa, CampoSeguro.Domain.Usuario Gestor, HttpClient Cliente)> EmpresaComGestor()
    {
        var empresa = await Pessoas.NovaEmpresa(banco);
        var gestor = await Pessoas.NovoUsuario(banco, empresa, CampoSeguro.Domain.Papel.Gestor);
        return (empresa, gestor, await ClienteAutenticado(gestor.Email, Pessoas.Senha));
    }

    public static string TokenDoLink(string link) => link.Split('#')[1];

    public Task<HttpResponseMessage> Login(string email, string senha) =>
        CreateClient().PostAsJsonAsync("/autenticacao/login", new { email, senha }, PlataformaApi.Json);

    public Task<HttpResponseMessage> Aceitar(string token, string senha) =>
        CreateClient().PostAsJsonAsync("/convites/aceitar", new { token, senha }, PlataformaApi.Json);

    public Task<HttpResponseMessage> Consultar(string token) =>
        CreateClient().PostAsJsonAsync("/convites/consultar", new { token }, PlataformaApi.Json);

    public async Task<HttpClient> ClienteAutenticado(string email, string senha)
    {
        var resposta = await Login(email, senha);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(resposta));
        return cliente;
    }

    public static async Task<string> Token(HttpResponseMessage login) =>
        (await login.Content.ReadFromJsonAsync<JsonElement>(PlataformaApi.Json)).GetProperty("token").GetString()!;

    public static async Task<string> Titulo(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<JsonElement>(PlataformaApi.Json)).GetProperty("title").GetString()!;
}
