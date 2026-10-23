using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampoSeguro.Domain;
using CampoSeguro.Platform.Api.Empresas;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;

namespace CampoSeguro.Testes.Gestao;

public sealed class ConvitesTestes(BancoDeTeste banco) : IAsyncDisposable
{
    private const string LinkInvalido = "Este link é inválido, expirou ou já foi usado. Peça um novo a quem enviou.";
    private const string NovaSenha = "senha-nova-do-gestor-1";

    private readonly GestaoApi gestao = new(banco);
    private readonly PlataformaApi plataforma = new(banco);

    public async ValueTask DisposeAsync()
    {
        await gestao.DisposeAsync();
        await plataforma.DisposeAsync();
    }

    [Fact]
    public async Task Fluxo_completo_da_plataforma_ao_painel()
    {
        banco.Exigir();
        var admin = await plataforma.ClienteAutenticado();
        var email = Gerar.Email("gil");
        var criada = (await (await admin.PostAsJsonAsync("/empresas",
                new NovaEmpresa("Telecom Alfa", Gerar.Cnpj(), new NovoGestor("Gil Gestor", email)), PlataformaApi.Json))
            .Content.ReadFromJsonAsync<EmpresaCriada>(PlataformaApi.Json))!;
        var token = criada.Convite.Link.Split('#')[1];

        // Antes de aceitar, o gestor não entra.
        Assert.Equal(HttpStatusCode.Unauthorized, (await gestao.Login(email, NovaSenha)).StatusCode);

        var consulta = await gestao.Consultar(token);
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        var dados = await consulta.Content.ReadFromJsonAsync<JsonElement>(PlataformaApi.Json);
        Assert.Equal("Gil Gestor", dados.GetProperty("nome").GetString());
        Assert.Equal("Telecom Alfa", dados.GetProperty("empresa").GetString());
        Assert.Equal("Convite", dados.GetProperty("tipo").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await gestao.Aceitar(token, NovaSenha)).StatusCode);

        var cliente = await gestao.ClienteAutenticado(email, NovaSenha);
        var eu = await cliente.GetFromJsonAsync<JsonElement>("/eu", PlataformaApi.Json);
        Assert.Equal(criada.Gestor.Id, eu.GetProperty("id").GetGuid());
        Assert.Equal("Gestor", eu.GetProperty("papel").GetString());
        Assert.Equal(criada.Empresa.Id, eu.GetProperty("empresa").GetProperty("id").GetGuid());

        // E a Plataforma passa a mostrar o gestor como ativo.
        var detalhe = (await admin.GetFromJsonAsync<DetalheEmpresa>($"/empresas/{criada.Empresa.Id}", PlataformaApi.Json))!;
        Assert.Equal(SituacaoGestor.Ativo, Assert.Single(detalhe.Gestores).Situacao);
    }

    [Fact]
    public async Task Senha_fraca_e_recusada_e_o_link_continua_valendo()
    {
        var (_, token) = await GestorConvidado();

        var resposta = await gestao.Aceitar(token, "curta");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.True((await resposta.Content.ReadFromJsonAsync<JsonElement>(PlataformaApi.Json)).GetProperty("errors").TryGetProperty("senha", out _));
        Assert.Equal(HttpStatusCode.OK, (await gestao.Consultar(token)).StatusCode);
    }

    [Fact]
    public async Task Link_usado_nao_vale_de_novo()
    {
        var (gestor, token) = await GestorConvidado();
        Assert.Equal(HttpStatusCode.NoContent, (await gestao.Aceitar(token, NovaSenha)).StatusCode);

        var outraVez = await gestao.Aceitar(token, "senha-de-quem-pegou-o-link");

        Assert.Equal(HttpStatusCode.BadRequest, outraVez.StatusCode);
        Assert.Equal(LinkInvalido, await GestaoApi.Titulo(outraVez));
        Assert.Equal(HttpStatusCode.BadRequest, (await gestao.Consultar(token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gestao.Login(gestor.Email, NovaSenha)).StatusCode);
    }

    [Fact]
    public async Task Link_vencido_revogado_ou_inventado_da_a_mesma_resposta()
    {
        var (vencido, tokenVencido) = await GestorConvidado();
        await banco.Sql($"UPDATE convite SET criado_em = now() - interval '4 days', expira_em = now() - interval '1 day' WHERE usuario_id = '{vencido.Id}'");
        var (revogado, tokenRevogado) = await GestorConvidado();
        await banco.Sql($"UPDATE convite SET revogado_em = now() WHERE usuario_id = '{revogado.Id}'");

        foreach (var token in new[] { tokenVencido, tokenRevogado, CampoSeguro.Data.Seguranca.Tokens.Gerar(), "" })
        {
            var resposta = await gestao.Aceitar(token, NovaSenha);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.Equal(LinkInvalido, await GestaoApi.Titulo(resposta));
        }
    }

    [Fact]
    public async Task Colaborador_desativado_nao_aceita_convite()
    {
        var (gestor, token) = await GestorConvidado();
        await banco.Sql($"UPDATE usuario SET ativo = false, desativado_em = now() WHERE id = '{gestor.Id}'");

        Assert.Equal(HttpStatusCode.BadRequest, (await gestao.Aceitar(token, NovaSenha)).StatusCode);
    }

    [Fact]
    public async Task Duas_aberturas_simultaneas_do_link_so_uma_define_a_senha()
    {
        var (gestor, token) = await GestorConvidado();

        var respostas = await Task.WhenAll(
            gestao.Aceitar(token, "senha-da-primeira-aba"),
            gestao.Aceitar(token, "senha-da-segunda-aba"));

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.BadRequest], respostas.Select(r => r.StatusCode).Order());
        var vencedora = respostas[0].StatusCode == HttpStatusCode.NoContent ? "senha-da-primeira-aba" : "senha-da-segunda-aba";
        Assert.Equal(HttpStatusCode.OK, (await gestao.Login(gestor.Email, vencedora)).StatusCode);
    }

    [Fact]
    public async Task Redefinir_senha_troca_a_senha_e_derruba_os_aparelhos_conectados()
    {
        var empresa = await Pessoas.NovaEmpresa(banco);
        var tecnico = await Pessoas.NovoUsuario(banco, empresa, Papel.Tecnico);
        await banco.Sql($"""
            INSERT INTO sessao_dispositivo (id, tenant_id, usuario_id, token_hash, expira_em, criado_em)
            VALUES (gen_random_uuid(), '{empresa}', '{tecnico.Id}', {Gerar.Hash(0x5e)}, now() + interval '30 days', now())
            """);
        var token = await Pessoas.NovoConvite(banco, tecnico, TipoConvite.RedefinicaoSenha);

        Assert.Equal(HttpStatusCode.NoContent, (await gestao.Aceitar(token, NovaSenha)).StatusCode);

        Assert.Equal("0", await banco.Valor($"SELECT count(*) FROM sessao_dispositivo WHERE usuario_id = '{tecnico.Id}' AND revogada_em IS NULL"));
        // Técnico não entra no Painel, mas a resposta 403 prova que a senha nova é a que vale.
        Assert.Equal(HttpStatusCode.Forbidden, (await gestao.Login(tecnico.Email, NovaSenha)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await gestao.Login(tecnico.Email, Pessoas.Senha)).StatusCode);
    }

    private async Task<(Usuario Gestor, string Token)> GestorConvidado()
    {
        var empresa = await Pessoas.NovaEmpresa(banco);
        var gestor = await Pessoas.NovoUsuario(banco, empresa, Papel.Gestor, comSenha: false);
        return (gestor, await Pessoas.NovoConvite(banco, gestor));
    }
}
