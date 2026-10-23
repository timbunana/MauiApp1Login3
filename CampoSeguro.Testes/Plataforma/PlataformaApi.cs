using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampoSeguro.Data;
using CampoSeguro.Data.Seguranca;
using CampoSeguro.Domain;
using CampoSeguro.Testes.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CampoSeguro.Testes.Plataforma;

/// <summary>
/// A Platform.Api inteira em memória, apontando para o banco de teste. O tipo genérico só indica o
/// assembly da API: não dá para usar <c>Program</c>, que existe, global, em cada uma das APIs.
/// </summary>
public sealed class PlataformaApi(BancoDeTeste banco, int tentativasLogin = 1000)
    : WebApplicationFactory<CampoSeguro.Platform.Api.Autenticacao.OpcoesJwt>
{
    public const string UrlAceite = "https://gestao.teste/convite";
    public const string Senha = "senha-longa-de-teste-1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting(CampoSeguroDb.VariavelConexao, banco.Conexao);
        builder.UseSetting("Jwt:Chave", "chave-de-teste-com-mais-de-32-bytes-0123456789");
        builder.UseSetting("Convites:UrlAceite", UrlAceite);
        builder.UseSetting("Login:TentativasPorMinuto", tentativasLogin.ToString());
    }

    /// <summary>Administrador novo, gravado direto no banco como faria o <c>--criar-admin</c>.</summary>
    public async Task<(Guid Id, string Email)> NovoAdministrador()
    {
        var email = Gerar.Email("admin");
        var admin = new AdministradorPlataforma
        {
            Nome = "Admin de Teste",
            Email = email,
            EmailNormalizado = Emails.Normalizar(email),
            SenhaHash = Senhas.Gerar(Senha),
        };

        await using var db = banco.Contexto(null);
        db.AdministradoresPlataforma.Add(admin);
        await db.SaveChangesAsync();
        return (admin.Id, email);
    }

    public async Task<HttpResponseMessage> Login(string email, string senha) =>
        await CreateClient().PostAsJsonAsync("/autenticacao/login", new { email, senha }, Json);

    /// <summary>Cliente já autenticado com um administrador novo.</summary>
    public async Task<HttpClient> ClienteAutenticado()
    {
        var (_, email) = await NovoAdministrador();
        var resposta = await Login(email, Senha);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var token = (await resposta.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString();
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }
}
