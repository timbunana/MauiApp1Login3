using System.Net;
using System.Net.Http.Json;
using CampoSeguro.Domain;
using CampoSeguro.Manager.Api.Colaboradores;
using CampoSeguro.Testes.Campo;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;

namespace CampoSeguro.Testes.Gestao;

public sealed class SenhasESessoesTestes(BancoDeTeste banco) : IAsyncDisposable
{
    private const string SenhaNova = "senha-trocada-pelo-usuario-1";

    private readonly GestaoApi gestao = new(banco);
    private readonly CampoApi campo = new(banco);

    public async ValueTask DisposeAsync()
    {
        await gestao.DisposeAsync();
        await campo.DisposeAsync();
    }

    [Fact]
    public async Task Gestor_troca_a_propria_senha()
    {
        banco.Exigir();
        var (_, gestor, cliente) = await gestao.EmpresaComGestor();

        var errada = await cliente.PostAsJsonAsync("/autenticacao/senha", new { senhaAtual = "nao-e-essa-123", senhaNova = SenhaNova }, PlataformaApi.Json);
        Assert.Equal(HttpStatusCode.BadRequest, errada.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await cliente.PostAsJsonAsync("/autenticacao/senha", new { senhaAtual = Pessoas.Senha, senhaNova = "curta" }, PlataformaApi.Json)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await cliente.PostAsJsonAsync("/autenticacao/senha", new { senhaAtual = Pessoas.Senha, senhaNova = SenhaNova }, PlataformaApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gestao.Login(gestor.Email, SenhaNova)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await gestao.Login(gestor.Email, Pessoas.Senha)).StatusCode);
    }

    [Fact]
    public async Task Tecnico_troca_a_senha_no_app_e_os_outros_aparelhos_saem()
    {
        banco.Exigir();
        var (_, tecnico, celular, clienteCelular) = await campo.TecnicoLogado();
        var tablet = await campo.Sessao(tecnico.Email);

        var resposta = await clienteCelular.PostAsJsonAsync("/autenticacao/senha", new { senhaAtual = Pessoas.Senha, senhaNova = SenhaNova }, PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clienteCelular.GetAsync("/catalogos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await campo.Renovar(celular.TokenRenovacao)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await campo.Cliente(tablet.TokenAcesso).GetAsync("/catalogos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await campo.Login(tecnico.Email, SenhaNova)).StatusCode);
    }

    [Fact]
    public async Task Gestor_ve_e_revoga_o_aparelho_perdido_do_tecnico()
    {
        banco.Exigir();
        var (empresa, _, painel) = await gestao.EmpresaComGestor();
        var (_, tecnico, perdido, clientePerdido) = await campo.TecnicoLogado(empresa);
        var novo = await campo.Sessao(tecnico.Email);

        var sessoes = (await painel.GetFromJsonAsync<List<SessaoDoAparelho>>($"/colaboradores/{tecnico.Id}/sessoes", PlataformaApi.Json))!;
        Assert.Equal(2, sessoes.Count);
        Assert.All(sessoes, s => Assert.True(s.Ativa));
        Assert.All(sessoes, s => Assert.Equal("Moto G (Android 14)", s.Dispositivo));

        var idPerdido = await banco.Valor($"SELECT id FROM sessao_dispositivo WHERE usuario_id = '{tecnico.Id}' ORDER BY criado_em LIMIT 1");
        Assert.Equal(HttpStatusCode.NoContent, (await painel.PostAsync($"/colaboradores/{tecnico.Id}/sessoes/{idPerdido}/revogar", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await clientePerdido.GetAsync("/catalogos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await campo.Renovar(perdido.TokenRenovacao)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await campo.Cliente(novo.TokenAcesso).GetAsync("/catalogos")).StatusCode);
        Assert.Single((await painel.GetFromJsonAsync<List<SessaoDoAparelho>>($"/colaboradores/{tecnico.Id}/sessoes", PlataformaApi.Json))!, s => s.Ativa);
    }

    [Fact]
    public async Task Gestor_de_outra_empresa_nao_ve_nem_revoga_sessoes()
    {
        banco.Exigir();
        var (empresa, _, _) = await gestao.EmpresaComGestor();
        var (_, tecnico, _, cliente) = await campo.TecnicoLogado(empresa);
        var (_, _, outroPainel) = await gestao.EmpresaComGestor();
        var sessao = await banco.Valor($"SELECT id FROM sessao_dispositivo WHERE usuario_id = '{tecnico.Id}'");

        Assert.Equal(HttpStatusCode.NotFound, (await outroPainel.GetAsync($"/colaboradores/{tecnico.Id}/sessoes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outroPainel.PostAsync($"/colaboradores/{tecnico.Id}/sessoes/{sessao}/revogar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/catalogos")).StatusCode);
    }
}
