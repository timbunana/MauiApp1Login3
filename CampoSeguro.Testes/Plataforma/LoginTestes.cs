using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CampoSeguro.Testes.Infra;

namespace CampoSeguro.Testes.Plataforma;

public sealed class LoginTestes(BancoDeTeste banco) : IAsyncDisposable
{
    private readonly PlataformaApi api = new(banco);

    public ValueTask DisposeAsync() => api.DisposeAsync();

    [Fact]
    public async Task Rota_sem_token_responde_401()
    {
        banco.Exigir();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/empresas")).StatusCode);
    }

    [Fact]
    public async Task Login_aceita_email_em_qualquer_caixa_e_com_espacos()
    {
        banco.Exigir();
        var (_, email) = await api.NovoAdministrador();

        var resposta = await api.Login($"  {email.ToUpperInvariant()} ", PlataformaApi.Senha);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(PlataformaApi.Json);
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("token").GetString()));
        Assert.True(corpo.GetProperty("expiraEm").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddHours(7));
    }

    [Fact]
    public async Task Senha_errada_e_email_inexistente_dao_a_mesma_resposta()
    {
        banco.Exigir();
        var (_, email) = await api.NovoAdministrador();

        var senhaErrada = await api.Login(email, "senha-errada-123");
        var semCadastro = await api.Login(Gerar.Email("ninguem"), PlataformaApi.Senha);

        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, semCadastro.StatusCode);
        var titulo = (await senhaErrada.Content.ReadFromJsonAsync<JsonElement>(PlataformaApi.Json)).GetProperty("title").GetString();
        Assert.Equal("E-mail ou senha incorretos.", titulo);
        Assert.Equal(titulo, (await semCadastro.Content.ReadFromJsonAsync<JsonElement>(PlataformaApi.Json)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Token_adulterado_e_recusado()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var partes = cliente.DefaultRequestHeaders.Authorization!.Parameter!.Split('.');
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", $"{partes[0]}.{partes[1]}x.{partes[2]}");

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/empresas")).StatusCode);
    }

    [Fact]
    public async Task Desativar_o_administrador_corta_o_token_na_hora()
    {
        banco.Exigir();
        var (id, email) = await api.NovoAdministrador();
        var token = (await (await api.Login(email, PlataformaApi.Senha)).Content.ReadFromJsonAsync<JsonElement>(PlataformaApi.Json))
            .GetProperty("token").GetString();
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await banco.Sql($"UPDATE administrador_plataforma SET ativo = false WHERE id = '{id}'");
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/empresas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Login(email, PlataformaApi.Senha)).StatusCode);

        await banco.Sql($"UPDATE administrador_plataforma SET ativo = true WHERE id = '{id}'");
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/empresas")).StatusCode);
    }

    [Fact]
    public async Task Excesso_de_tentativas_de_login_responde_429()
    {
        banco.Exigir();
        await using var limitada = new PlataformaApi(banco, tentativasLogin: 3);

        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            codigos.Add((await limitada.Login("x@teste.com.br", "qualquer-coisa-1")).StatusCode);

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], codigos);
    }
}
