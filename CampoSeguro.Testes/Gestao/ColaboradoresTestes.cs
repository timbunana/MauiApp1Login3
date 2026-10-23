using System.Net;
using System.Net.Http.Json;
using CampoSeguro.Domain;
using CampoSeguro.Manager.Api.Colaboradores;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;

namespace CampoSeguro.Testes.Gestao;

public sealed class ColaboradoresTestes(BancoDeTeste banco) : IAsyncDisposable
{
    private const string SenhaNova = "senha-nova-do-tecnico-1";

    private readonly GestaoApi gestao = new(banco);

    public ValueTask DisposeAsync() => gestao.DisposeAsync();

    // --- Cadastro

    [Fact]
    public async Task Cadastra_tecnico_que_aceita_o_convite_e_fica_ativo()
    {
        banco.Exigir();
        var (empresa, _, cliente) = await gestao.EmpresaComGestor();
        var email = Gerar.Email("tec");

        var resposta = await cliente.PostAsJsonAsync("/colaboradores", new { nome = "Téo Técnico", email }, PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var convidado = (await resposta.Content.ReadFromJsonAsync<ColaboradorConvidado>(PlataformaApi.Json))!;
        Assert.Equal(Papel.Tecnico, convidado.Colaborador.Papel);
        Assert.Equal(SituacaoAcesso.ConvitePendente, convidado.Colaborador.Situacao);
        Assert.Equal(empresa.ToString(), await banco.Valor($"SELECT tenant_id FROM usuario WHERE id = '{convidado.Colaborador.Id}'"));

        Assert.Equal(HttpStatusCode.NoContent, (await gestao.Aceitar(GestaoApi.TokenDoLink(convidado.Convite.Link), SenhaNova)).StatusCode);

        var lista = (await cliente.GetFromJsonAsync<List<Colaborador>>("/colaboradores", PlataformaApi.Json))!;
        Assert.Equal(SituacaoAcesso.Ativo, lista.Single(c => c.Id == convidado.Colaborador.Id).Situacao);
    }

    [Fact]
    public async Task Gestor_pode_cadastrar_outro_gestor()
    {
        banco.Exigir();
        var (_, _, cliente) = await gestao.EmpresaComGestor();

        var resposta = await cliente.PostAsJsonAsync("/colaboradores", new NovoColaborador("Outra Gestora", Gerar.Email(), Papel.Gestor), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal(Papel.Gestor, (await resposta.Content.ReadFromJsonAsync<ColaboradorConvidado>(PlataformaApi.Json))!.Colaborador.Papel);
    }

    [Fact]
    public async Task Dados_invalidos_voltam_400()
    {
        banco.Exigir();
        var (_, _, cliente) = await gestao.EmpresaComGestor();

        var resposta = await cliente.PostAsJsonAsync("/colaboradores", new { nome = "", email = "x@" }, PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Email_ativo_em_outra_empresa_bloqueia_ate_ser_desativado_la()
    {
        banco.Exigir();
        var (empresaA, _, clienteA) = await gestao.EmpresaComGestor();
        var (_, _, clienteB) = await gestao.EmpresaComGestor();
        var email = Gerar.Email("troca-de-empresa");
        var tecnicoNaA = await Cadastrar(clienteA, email);

        Assert.Equal(HttpStatusCode.Conflict,
            (await clienteB.PostAsJsonAsync("/colaboradores", new { nome = "Mesmo", email = email.ToUpperInvariant() }, PlataformaApi.Json)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await clienteA.PostAsync($"/colaboradores/{tecnicoNaA.Colaborador.Id}/desativar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await clienteB.PostAsJsonAsync("/colaboradores", new { nome = "Mesmo", email }, PlataformaApi.Json)).StatusCode);

        // E a empresa A não consegue mais reativar, porque o e-mail agora está ativo na B.
        Assert.Equal(HttpStatusCode.Conflict, (await clienteA.PostAsync($"/colaboradores/{tecnicoNaA.Colaborador.Id}/reativar", null)).StatusCode);
    }

    // --- Isolamento

    [Fact]
    public async Task Gestor_de_outra_empresa_nao_enxerga_nem_altera_o_colaborador()
    {
        banco.Exigir();
        var (_, _, clienteA) = await gestao.EmpresaComGestor();
        var (_, _, clienteB) = await gestao.EmpresaComGestor();
        var id = (await Cadastrar(clienteA)).Colaborador.Id;

        Assert.Equal(HttpStatusCode.NotFound, (await clienteB.GetAsync($"/colaboradores/{id}")).StatusCode);
        foreach (var acao in new[] { "convite", "redefinicao-senha", "desativar", "reativar", "anonimizar" })
            Assert.Equal(HttpStatusCode.NotFound, (await clienteB.PostAsync($"/colaboradores/{id}/{acao}", null)).StatusCode);

        Assert.DoesNotContain((await clienteB.GetFromJsonAsync<List<Colaborador>>("/colaboradores?incluirDesativados=true", PlataformaApi.Json))!,
            c => c.Id == id);
        Assert.Equal("true", await banco.Valor($"SELECT ativo::text FROM usuario WHERE id = '{id}'"));
    }

    // --- Links

    [Fact]
    public async Task Reenviar_convite_invalida_o_link_anterior()
    {
        banco.Exigir();
        var (_, _, cliente) = await gestao.EmpresaComGestor();
        var convidado = await Cadastrar(cliente);

        var resposta = await cliente.PostAsync($"/colaboradores/{convidado.Colaborador.Id}/convite", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var novo = (await resposta.Content.ReadFromJsonAsync<ConviteEmitido>(PlataformaApi.Json))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await gestao.Consultar(GestaoApi.TokenDoLink(convidado.Convite.Link))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gestao.Consultar(GestaoApi.TokenDoLink(novo.Link))).StatusCode);
    }

    [Fact]
    public async Task Convite_so_para_quem_nao_tem_senha_e_redefinicao_so_para_quem_tem()
    {
        banco.Exigir();
        var (empresa, _, cliente) = await gestao.EmpresaComGestor();
        var comSenha = await Pessoas.NovoUsuario(banco, empresa, Papel.Tecnico);
        var semSenha = (await Cadastrar(cliente)).Colaborador.Id;

        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync($"/colaboradores/{comSenha.Id}/convite", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync($"/colaboradores/{semSenha}/redefinicao-senha", null)).StatusCode);
    }

    [Fact]
    public async Task Redefinicao_de_senha_do_tecnico_funciona()
    {
        banco.Exigir();
        var (empresa, _, cliente) = await gestao.EmpresaComGestor();
        var tecnico = await Pessoas.NovoUsuario(banco, empresa, Papel.Tecnico);

        var resposta = await cliente.PostAsync($"/colaboradores/{tecnico.Id}/redefinicao-senha", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var link = (await resposta.Content.ReadFromJsonAsync<ConviteEmitido>(PlataformaApi.Json))!.Link;
        Assert.Equal(HttpStatusCode.NoContent, (await gestao.Aceitar(GestaoApi.TokenDoLink(link), SenhaNova)).StatusCode);
        // 403 = senha certa, mas técnico entra pelo app.
        Assert.Equal(HttpStatusCode.Forbidden, (await gestao.Login(tecnico.Email, SenhaNova)).StatusCode);
    }

    [Fact]
    public async Task Gestor_nao_gera_redefinicao_de_senha_de_outro_gestor()
    {
        banco.Exigir();
        var (empresa, _, cliente) = await gestao.EmpresaComGestor();
        var outroGestor = await Pessoas.NovoUsuario(banco, empresa, Papel.Gestor);

        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync($"/colaboradores/{outroGestor.Id}/redefinicao-senha", null)).StatusCode);
        Assert.Equal("0", await banco.Valor($"SELECT count(*) FROM convite WHERE usuario_id = '{outroGestor.Id}'"));
    }

    // --- Desativação

    [Fact]
    public async Task Desativar_corta_link_sessoes_e_some_da_lista()
    {
        banco.Exigir();
        var (empresa, _, cliente) = await gestao.EmpresaComGestor();
        var convidado = await Cadastrar(cliente);
        var id = convidado.Colaborador.Id;
        await banco.Sql($"""
            INSERT INTO sessao_dispositivo (id, tenant_id, usuario_id, token_hash, expira_em, criado_em)
            VALUES (gen_random_uuid(), '{empresa}', '{id}', {Gerar.Hash(0x7a)}, now() + interval '30 days', now())
            """);

        var resposta = await cliente.PostAsync($"/colaboradores/{id}/desativar", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(SituacaoAcesso.Desativado, (await resposta.Content.ReadFromJsonAsync<Colaborador>(PlataformaApi.Json))!.Situacao);
        Assert.Equal(HttpStatusCode.BadRequest, (await gestao.Consultar(GestaoApi.TokenDoLink(convidado.Convite.Link))).StatusCode);
        Assert.Equal("0", await banco.Valor($"SELECT count(*) FROM sessao_dispositivo WHERE usuario_id = '{id}' AND revogada_em IS NULL"));
        Assert.Equal("0", await banco.Valor($"SELECT count(*) FROM convite WHERE usuario_id = '{id}' AND revogado_em IS NULL"));

        Assert.DoesNotContain((await cliente.GetFromJsonAsync<List<Colaborador>>("/colaboradores", PlataformaApi.Json))!, c => c.Id == id);
        Assert.Contains((await cliente.GetFromJsonAsync<List<Colaborador>>("/colaboradores?incluirDesativados=true", PlataformaApi.Json))!, c => c.Id == id);
    }

    [Fact]
    public async Task Gestor_nao_desativa_a_si_mesmo()
    {
        banco.Exigir();
        var (_, gestor, cliente) = await gestao.EmpresaComGestor();

        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync($"/colaboradores/{gestor.Id}/desativar", null)).StatusCode);
    }

    [Fact]
    public async Task Reativar_devolve_o_acesso_com_a_senha_antiga()
    {
        banco.Exigir();
        var (empresa, _, cliente) = await gestao.EmpresaComGestor();
        var outroGestor = await Pessoas.NovoUsuario(banco, empresa, Papel.Gestor);
        await cliente.PostAsync($"/colaboradores/{outroGestor.Id}/desativar", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await gestao.Login(outroGestor.Email, Pessoas.Senha)).StatusCode);

        var resposta = await cliente.PostAsync($"/colaboradores/{outroGestor.Id}/reativar", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(SituacaoAcesso.Ativo, (await resposta.Content.ReadFromJsonAsync<Colaborador>(PlataformaApi.Json))!.Situacao);
        Assert.Equal(HttpStatusCode.OK, (await gestao.Login(outroGestor.Email, Pessoas.Senha)).StatusCode);
    }

    // --- Anonimização

    [Fact]
    public async Task Anonimizar_apaga_dados_pessoais_e_preserva_o_autor_da_apr()
    {
        var c = await Cenario.Criar(banco);
        var gestor = await Pessoas.NovoUsuario(banco, c.TenantA, Papel.Gestor);
        var cliente = await gestao.ClienteAutenticado(gestor.Email, Pessoas.Senha);
        var rota = $"/colaboradores/{c.TecnicoA}";

        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync($"{rota}/anonimizar", null)).StatusCode);
        await cliente.PostAsync($"{rota}/desativar", null);
        var resposta = await cliente.PostAsync($"{rota}/anonimizar", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var anonimo = (await resposta.Content.ReadFromJsonAsync<Colaborador>(PlataformaApi.Json))!;
        Assert.True(anonimo.Anonimizado);
        Assert.DoesNotContain("tecnico-a", anonimo.Email);
        Assert.Equal("Colaborador anonimizado", anonimo.Nome);
        Assert.Equal("Tec A", await banco.Valor($"SELECT autor_nome FROM apr WHERE id = '{c.Apr}'"));
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync($"{rota}/reativar", null)).StatusCode);
    }

    private static async Task<ColaboradorConvidado> Cadastrar(HttpClient cliente, string? email = null)
    {
        var resposta = await cliente.PostAsJsonAsync("/colaboradores", new { nome = "Técnico de Teste", email = email ?? Gerar.Email("tec") }, PlataformaApi.Json);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<ColaboradorConvidado>(PlataformaApi.Json))!;
    }
}
