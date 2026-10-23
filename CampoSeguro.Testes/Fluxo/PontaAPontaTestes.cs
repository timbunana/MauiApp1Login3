using System.Net;
using System.Net.Http.Json;
using CampoSeguro.Domain;
using CampoSeguro.Field.Api.Aprs;
using CampoSeguro.Manager.Api.Catalogos;
using CampoSeguro.Manager.Api.Colaboradores;
using CampoSeguro.Platform.Api.Empresas;
using CampoSeguro.Testes.Campo;
using CampoSeguro.Testes.Gestao;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;
using static CampoSeguro.Field.Api.Catalogos.CatalogosEndpoints;

namespace CampoSeguro.Testes.Fluxo;

/// <summary>
/// O caminho inteiro, só pelas três APIs, sem atalho no banco: da empresa cadastrada pela equipe
/// CampoSeguro até a APR com foto e PDF no servidor. Quando os repositórios forem separados, este é o
/// teste que vira teste de ponta a ponta do ambiente.
/// </summary>
public sealed class PontaAPontaTestes(BancoDeTeste banco) : IAsyncDisposable
{
    private const string SenhaGestor = "senha-da-gestora-123";
    private const string SenhaTecnico = "senha-do-tecnico-123";

    private readonly PlataformaApi plataforma = new(banco);
    private readonly GestaoApi gestao = new(banco);
    private readonly CampoApi campo = new(banco);

    public async ValueTask DisposeAsync()
    {
        await plataforma.DisposeAsync();
        await gestao.DisposeAsync();
        await campo.DisposeAsync();
    }

    [Fact]
    public async Task Da_empresa_nova_a_apr_sincronizada()
    {
        banco.Exigir();

        // 1. Equipe CampoSeguro cadastra a empresa e a primeira gestora.
        var admin = await plataforma.ClienteAutenticado();
        var emailGestora = Gerar.Email("gestora");
        var empresa = (await (await admin.PostAsJsonAsync("/empresas",
                new NovaEmpresa("Telecom Alfa", Gerar.Cnpj(), new NovoGestor("Gina Gestora", emailGestora)), PlataformaApi.Json))
            .Content.ReadFromJsonAsync<EmpresaCriada>(PlataformaApi.Json))!;

        // 2. Gestora aceita o convite, entra no Painel, importa e publica o checklist de altura.
        Assert.Equal(HttpStatusCode.NoContent, (await gestao.Aceitar(GestaoApi.TokenDoLink(empresa.Convite.Link), SenhaGestor)).StatusCode);
        var painel = await gestao.ClienteAutenticado(emailGestora, SenhaGestor);
        var catalogo = (await (await painel.PostAsync("/catalogos/modelos/telecom-altura", null)).Content.ReadFromJsonAsync<DetalheCatalogo>(PlataformaApi.Json))!;
        Assert.Equal(HttpStatusCode.OK, (await painel.PostAsync($"/catalogos/{catalogo.Id}/rascunho/publicar", null)).StatusCode);

        // 3. Gestora cadastra o técnico; ele aceita o convite.
        var emailTecnico = Gerar.Email("tecnico");
        var tecnico = (await (await painel.PostAsJsonAsync("/colaboradores", new { nome = "Téo Técnico", email = emailTecnico }, PlataformaApi.Json))
            .Content.ReadFromJsonAsync<ColaboradorConvidado>(PlataformaApi.Json))!;
        Assert.Equal(HttpStatusCode.NoContent, (await gestao.Aceitar(GestaoApi.TokenDoLink(tecnico.Convite.Link), SenhaTecnico)).StatusCode);

        // 4. No app: login, download do catálogo publicado.
        var sessao = await campo.Sessao(emailTecnico, SenhaTecnico);
        Assert.Equal("Telecom Alfa", sessao.Empresa.Nome);
        var app = campo.Cliente(sessao.TokenAcesso);
        var baixado = Assert.Single((await app.GetFromJsonAsync<List<CatalogoPublicado>>("/catalogos", PlataformaApi.Json))!);
        Assert.Equal(10, baixado.Itens.Count);

        // 5. Técnico preenche (um item não conforme), assina offline e sincroniza: APR, foto, PDF.
        var apr = new AprDeTeste
        {
            Autor = sessao.Tecnico.Id,
            Versao = baixado.VersaoId,
            Itens = baixado.Itens.Select(i => new Pessoas.ItemPublicado(i.ItemId, i.Ordem, i.Texto)).ToList(),
        }.ComFoto();
        var itens = apr.Itens.Select(i => new ItemEnviado(i.ItemId, i.Ordem, i.Texto,
            i.Ordem == 7 ? Resposta.NaoConforme : Resposta.Conforme, i.Ordem == 7 ? "Escada sem amarração; substituída." : null)).ToList();

        Assert.Equal(HttpStatusCode.Created, (await app.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(itens), PlataformaApi.Json)).StatusCode);
        var (foto, bytes) = apr.Fotos.Single();
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsync($"/aprs/{apr.Id}/evidencias/{foto}", AprDeTeste.Arquivo(bytes, "image/jpeg"))).StatusCode);
        var final = await app.PutAsync($"/aprs/{apr.Id}/pdf", AprDeTeste.Arquivo(AprDeTeste.Pdf, "application/pdf"));

        var situacao = (await final.Content.ReadFromJsonAsync<SituacaoApr>(PlataformaApi.Json))!;
        Assert.Empty(situacao.EvidenciasPendentes);
        Assert.False(situacao.PdfPendente);

        // 6. No banco: a APR é da empresa certa, com o autor congelado e a não conformidade no item de origem.
        Assert.Equal($"{empresa.Empresa.Id}|Téo Técnico|1",
            await banco.Valor($"""
                SELECT a.tenant_id || '|' || a.autor_nome || '|' || count(*) FILTER (WHERE i.resposta = 'NaoConforme')
                FROM apr a JOIN apr_item i ON i.apr_id = a.id WHERE a.id = '{apr.Id}' GROUP BY a.tenant_id, a.autor_nome
                """));
    }
}
