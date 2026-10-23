using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CampoSeguro.Domain;
using CampoSeguro.Platform.Api.Empresas;
using CampoSeguro.Testes.Infra;

namespace CampoSeguro.Testes.Plataforma;

public sealed class EmpresasTestes(BancoDeTeste banco) : IAsyncDisposable
{
    private readonly PlataformaApi api = new(banco);

    public ValueTask DisposeAsync() => api.DisposeAsync();

    // --- Criação

    [Fact]
    public async Task Cria_empresa_com_gestor_e_convite()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var cnpj = Gerar.Cnpj();
        var mascarado = $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";
        var email = Gerar.Email("Gestor");

        var resposta = await cliente.PostAsJsonAsync("/empresas", new NovaEmpresa("Telecom Alfa", mascarado, new NovoGestor("Gil Gestor", email)), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var criada = (await resposta.Content.ReadFromJsonAsync<EmpresaCriada>(PlataformaApi.Json))!;
        Assert.Equal($"/empresas/{criada.Empresa.Id}", resposta.Headers.Location?.OriginalString);
        Assert.Equal(cnpj, criada.Empresa.Documento);
        Assert.Equal(SituacaoAcesso.ConvitePendente, criada.Gestor.Situacao);

        // O token vai no fragmento, e o banco guarda só o SHA-256 dele.
        Assert.StartsWith($"{PlataformaApi.UrlAceite}#", criada.Convite.Link);
        var token = criada.Convite.Link.Split('#')[1];
        Assert.Equal(43, token.Length);
        var hashEsperado = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        Assert.Equal(hashEsperado, await banco.Valor($"SELECT encode(token_hash, 'hex') FROM convite WHERE usuario_id = '{criada.Gestor.Id}'"));

        Assert.Equal($"{criada.Empresa.Id}|Gestor||{email.ToLowerInvariant()}",
            await banco.Valor($"SELECT tenant_id || '|' || papel || '|' || coalesce(senha_hash, '') || '|' || email_normalizado FROM usuario WHERE id = '{criada.Gestor.Id}'"));
        Assert.Equal("72", await banco.Valor($"SELECT round(extract(epoch FROM expira_em - criado_em) / 3600) FROM convite WHERE usuario_id = '{criada.Gestor.Id}'"));
    }

    [Fact]
    public async Task Dados_invalidos_voltam_400_com_os_campos()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();

        var resposta = await cliente.PostAsJsonAsync("/empresas", new { nome = " ", documento = "11.222.333/0001-00" }, PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var erros = (await resposta.Content.ReadFromJsonAsync<JsonElement>(PlataformaApi.Json)).GetProperty("errors");
        Assert.True(erros.TryGetProperty("nome", out _));
        Assert.True(erros.TryGetProperty("documento", out _));
        Assert.True(erros.TryGetProperty("gestor", out _));
    }

    [Fact]
    public async Task Cnpj_de_digitos_repetidos_e_recusado()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();

        var resposta = await cliente.PostAsJsonAsync("/empresas", new NovaEmpresa("X", "11111111111111", new NovoGestor("G", Gerar.Email())), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Cnpj_repetido_responde_409()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var cnpj = Gerar.Cnpj();
        await Criar(cliente, cnpj);

        var resposta = await cliente.PostAsJsonAsync("/empresas", new NovaEmpresa("Cópia", cnpj, new NovoGestor("Outro", Gerar.Email())), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    [Fact]
    public async Task Email_ativo_em_outra_empresa_responde_409_e_nao_grava_nada()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var email = Gerar.Email();
        await Criar(cliente, gestorEmail: email);
        var cnpj = Gerar.Cnpj();

        var resposta = await cliente.PostAsJsonAsync("/empresas", new NovaEmpresa("Beta", cnpj, new NovoGestor("Gil", email.ToUpperInvariant())), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("0", await banco.Valor($"SELECT count(*) FROM tenant WHERE documento = '{cnpj}'"));
    }

    // --- Consulta

    [Fact]
    public async Task Lista_e_detalha_a_empresa()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var criada = await Criar(cliente);

        var lista = await cliente.GetFromJsonAsync<List<ResumoEmpresa>>("/empresas", PlataformaApi.Json);
        Assert.Contains(lista!, e => e.Id == criada.Empresa.Id);

        var detalhe = (await cliente.GetFromJsonAsync<DetalheEmpresa>($"/empresas/{criada.Empresa.Id}", PlataformaApi.Json))!;
        var gestor = Assert.Single(detalhe.Gestores);
        Assert.Equal(SituacaoAcesso.ConvitePendente, gestor.Situacao);
        // O banco guarda microssegundos; o .NET, 100 ns.
        Assert.Equal(criada.Convite.ExpiraEm, gestor.ConviteExpiraEm!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Empresa_inexistente_responde_404()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();

        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/empresas/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await cliente.PostAsJsonAsync($"/empresas/{Guid.NewGuid()}/gestores", new NovoGestor("X", Gerar.Email()), PlataformaApi.Json)).StatusCode);
    }

    [Fact]
    public async Task Convite_vencido_aparece_como_expirado()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var criada = await Criar(cliente);

        await banco.Sql($"UPDATE convite SET criado_em = now() - interval '4 days', expira_em = now() - interval '1 day' WHERE usuario_id = '{criada.Gestor.Id}'");

        var detalhe = (await cliente.GetFromJsonAsync<DetalheEmpresa>($"/empresas/{criada.Empresa.Id}", PlataformaApi.Json))!;
        Assert.Equal(SituacaoAcesso.ConviteExpirado, Assert.Single(detalhe.Gestores).Situacao);
    }

    // --- Reenvio

    [Fact]
    public async Task Reenvio_revoga_o_convite_anterior_e_emite_outro()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var criada = await Criar(cliente);
        var rota = $"/empresas/{criada.Empresa.Id}/gestores/{criada.Gestor.Id}/convite";

        var primeiro = await cliente.PostAsync(rota, null);
        var segundo = await cliente.PostAsync(rota, null);

        Assert.Equal(HttpStatusCode.OK, primeiro.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
        var links = new[] { criada.Convite.Link, (await primeiro.Content.ReadFromJsonAsync<ConviteEmitido>(PlataformaApi.Json))!.Link,
                            (await segundo.Content.ReadFromJsonAsync<ConviteEmitido>(PlataformaApi.Json))!.Link };
        Assert.Equal(3, links.Distinct().Count());
        Assert.Equal("2/1", await banco.Valor(
            $"SELECT count(*) FILTER (WHERE revogado_em IS NOT NULL) || '/' || count(*) FILTER (WHERE revogado_em IS NULL) FROM convite WHERE usuario_id = '{criada.Gestor.Id}'"));
    }

    [Fact]
    public async Task Reenvio_para_gestor_inexistente_responde_404()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var criada = await Criar(cliente);

        var resposta = await cliente.PostAsync($"/empresas/{criada.Empresa.Id}/gestores/{Guid.NewGuid()}/convite", null);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Reenvio_recusa_gestor_que_ja_tem_senha_ou_esta_desativado()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var criada = await Criar(cliente);
        var rota = $"/empresas/{criada.Empresa.Id}/gestores/{criada.Gestor.Id}/convite";

        await banco.Sql($"UPDATE usuario SET senha_hash = 'x' WHERE id = '{criada.Gestor.Id}'");
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync(rota, null)).StatusCode);
        var detalhe = (await cliente.GetFromJsonAsync<DetalheEmpresa>($"/empresas/{criada.Empresa.Id}", PlataformaApi.Json))!;
        Assert.Equal(SituacaoAcesso.Ativo, Assert.Single(detalhe.Gestores).Situacao);

        await banco.Sql($"UPDATE usuario SET ativo = false, desativado_em = now() WHERE id = '{criada.Gestor.Id}'");
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync(rota, null)).StatusCode);
    }

    // --- Gestor adicional

    [Fact]
    public async Task Convida_segundo_gestor()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var criada = await Criar(cliente);

        var resposta = await cliente.PostAsJsonAsync($"/empresas/{criada.Empresa.Id}/gestores", new NovoGestor("Hel Gestora", Gerar.Email()), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var convidado = (await resposta.Content.ReadFromJsonAsync<GestorConvidado>(PlataformaApi.Json))!;
        Assert.StartsWith($"{PlataformaApi.UrlAceite}#", convidado.Convite.Link);
        Assert.Equal(criada.Empresa.Id.ToString(), await banco.Valor($"SELECT tenant_id FROM usuario WHERE id = '{convidado.Gestor.Id}'"));
    }

    [Fact]
    public async Task Gestor_com_email_invalido_responde_400()
    {
        banco.Exigir();
        var cliente = await api.ClienteAutenticado();
        var criada = await Criar(cliente);

        var resposta = await cliente.PostAsJsonAsync($"/empresas/{criada.Empresa.Id}/gestores", new NovoGestor("X", "x@"), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    private static async Task<EmpresaCriada> Criar(HttpClient cliente, string? cnpj = null, string? gestorEmail = null)
    {
        var resposta = await cliente.PostAsJsonAsync("/empresas",
            new NovaEmpresa("Empresa de Teste", cnpj ?? Gerar.Cnpj(), new NovoGestor("Gestor de Teste", gestorEmail ?? Gerar.Email())),
            PlataformaApi.Json);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<EmpresaCriada>(PlataformaApi.Json))!;
    }
}
