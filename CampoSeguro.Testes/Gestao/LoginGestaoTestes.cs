using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CampoSeguro.Domain;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;

namespace CampoSeguro.Testes.Gestao;

public sealed class LoginGestaoTestes(BancoDeTeste banco) : IAsyncDisposable
{
    private readonly GestaoApi gestao = new(banco);

    public ValueTask DisposeAsync() => gestao.DisposeAsync();

    [Fact]
    public async Task Rota_sem_token_responde_401()
    {
        banco.Exigir();
        Assert.Equal(HttpStatusCode.Unauthorized, (await gestao.CreateClient().GetAsync("/eu")).StatusCode);
    }

    [Fact]
    public async Task Gestor_entra_e_ve_so_a_propria_empresa()
    {
        var empresaA = await Pessoas.NovaEmpresa(banco, "Empresa A");
        await Pessoas.NovaEmpresa(banco, "Empresa B");
        var gestor = await Pessoas.NovoUsuario(banco, empresaA, Papel.Gestor);

        var cliente = await gestao.ClienteAutenticado($" {gestor.Email.ToUpperInvariant()} ", Pessoas.Senha);
        var eu = await cliente.GetFromJsonAsync<JsonElement>("/eu", PlataformaApi.Json);

        Assert.Equal(empresaA, eu.GetProperty("empresa").GetProperty("id").GetGuid());
        Assert.Equal("Empresa A", eu.GetProperty("empresa").GetProperty("nome").GetString());
    }

    [Fact]
    public async Task Senha_errada_convite_pendente_e_desativado_dao_a_mesma_resposta()
    {
        var empresa = await Pessoas.NovaEmpresa(banco);
        var comSenha = await Pessoas.NovoUsuario(banco, empresa, Papel.Gestor);
        var semSenha = await Pessoas.NovoUsuario(banco, empresa, Papel.Gestor, comSenha: false);
        var desativado = await Pessoas.NovoUsuario(banco, empresa, Papel.Gestor);
        await banco.Sql($"UPDATE usuario SET ativo = false, desativado_em = now() WHERE id = '{desativado.Id}'");

        foreach (var (email, senha) in new[] { (comSenha.Email, "senha-errada-123"), (semSenha.Email, Pessoas.Senha), (desativado.Email, Pessoas.Senha), (Gerar.Email(), Pessoas.Senha) })
        {
            var resposta = await gestao.Login(email, senha);
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
            Assert.Equal("E-mail ou senha incorretos.", await GestaoApi.Titulo(resposta));
        }
    }

    [Fact]
    public async Task Tecnico_com_senha_certa_e_mandado_para_o_app()
    {
        var empresa = await Pessoas.NovaEmpresa(banco);
        var tecnico = await Pessoas.NovoUsuario(banco, empresa, Papel.Tecnico);

        var resposta = await gestao.Login(tecnico.Email, Pessoas.Senha);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("Seu acesso é pelo aplicativo CampoSeguro.", await GestaoApi.Titulo(resposta));
    }

    [Fact]
    public async Task Desativar_o_gestor_corta_o_token_na_hora()
    {
        var empresa = await Pessoas.NovaEmpresa(banco);
        var gestor = await Pessoas.NovoUsuario(banco, empresa, Papel.Gestor);
        var cliente = await gestao.ClienteAutenticado(gestor.Email, Pessoas.Senha);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/eu")).StatusCode);

        await banco.Sql($"UPDATE usuario SET ativo = false, desativado_em = now() WHERE id = '{gestor.Id}'");

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/eu")).StatusCode);
    }

    [Fact]
    public async Task Token_de_uma_aplicacao_nao_abre_a_outra()
    {
        var empresa = await Pessoas.NovaEmpresa(banco);
        var gestor = await Pessoas.NovoUsuario(banco, empresa, Papel.Gestor);
        await using var plataforma = new PlataformaApi(banco);

        var tokenGestao = await GestaoApi.Token(await gestao.Login(gestor.Email, Pessoas.Senha));
        var clientePlataforma = plataforma.CreateClient();
        clientePlataforma.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenGestao);
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientePlataforma.GetAsync("/empresas")).StatusCode);

        var tokenPlataforma = (await plataforma.ClienteAutenticado()).DefaultRequestHeaders.Authorization!.Parameter;
        var clienteGestao = gestao.CreateClient();
        clienteGestao.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenPlataforma);
        Assert.Equal(HttpStatusCode.Unauthorized, (await clienteGestao.GetAsync("/eu")).StatusCode);
    }
}
