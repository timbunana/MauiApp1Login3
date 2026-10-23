using System.Net;
using System.Net.Http.Json;
using CampoSeguro.Manager.Api.Catalogos;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;

namespace CampoSeguro.Testes.Gestao;

public sealed class CatalogosTestes(BancoDeTeste banco) : IAsyncDisposable
{
    private readonly GestaoApi gestao = new(banco);

    public ValueTask DisposeAsync() => gestao.DisposeAsync();

    // --- Modelos

    [Fact]
    public async Task Modelo_e_importado_como_rascunho_para_a_empresa_revisar()
    {
        banco.Exigir();
        var (_, _, cliente) = await gestao.EmpresaComGestor();

        var resposta = await cliente.PostAsync("/catalogos/modelos/telecom-altura", null);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var catalogo = (await resposta.Content.ReadFromJsonAsync<DetalheCatalogo>(PlataformaApi.Json))!;
        Assert.Equal("Telecom — Altura", catalogo.Nome);
        var versao = Assert.Single(catalogo.Versoes);
        Assert.Null(versao.PublicadaEm);
        Assert.Equal(10, versao.QuantidadeItens);

        var rascunho = (await cliente.GetFromJsonAsync<Versao>($"/catalogos/{catalogo.Id}/rascunho", PlataformaApi.Json))!;
        Assert.Equal("Uso de Capacete com Jugular", rascunho.Itens[0].Texto);
        Assert.Equal(Enumerable.Range(1, 10), rascunho.Itens.Select(i => i.Ordem));

        var lista = (await cliente.GetFromJsonAsync<List<ResumoCatalogo>>("/catalogos", PlataformaApi.Json))!;
        var resumo = Assert.Single(lista);
        Assert.Null(resumo.Publicada);
        Assert.Equal(1, resumo.Rascunho!.Numero);
    }

    [Fact]
    public async Task Modelos_disponiveis_e_importacao_repetida()
    {
        banco.Exigir();
        var (_, _, cliente) = await gestao.EmpresaComGestor();

        var modelos = (await cliente.GetFromJsonAsync<List<ResumoModelo>>("/catalogos/modelos", PlataformaApi.Json))!;
        Assert.Equal(["telecom-altura", "telecom-mesmo-nivel"], modelos.Select(m => m.Codigo));
        Assert.Equal(4, modelos[1].QuantidadeItens);

        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsync("/catalogos/modelos/telecom-mesmo-nivel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync("/catalogos/modelos/telecom-mesmo-nivel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.PostAsync("/catalogos/modelos/nao-existe", null)).StatusCode);
    }

    // --- Ciclo de versões

    [Fact]
    public async Task Ciclo_completo_preserva_a_versao_publicada_e_a_identidade_dos_itens()
    {
        banco.Exigir();
        var (_, _, cliente) = await gestao.EmpresaComGestor();
        var id = await Importar(cliente, "telecom-mesmo-nivel");
        var v1 = (await cliente.GetFromJsonAsync<Versao>($"/catalogos/{id}/rascunho", PlataformaApi.Json))!;

        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsync($"/catalogos/{id}/rascunho/publicar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PutAsJsonAsync($"/catalogos/{id}/rascunho", new EdicaoRascunho([]), PlataformaApi.Json)).StatusCode);

        // Versão 2 nasce com os itens da 1.
        var aberto = await cliente.PostAsync($"/catalogos/{id}/rascunho", null);
        Assert.Equal(HttpStatusCode.Created, aberto.StatusCode);
        var v2 = (await aberto.Content.ReadFromJsonAsync<Versao>(PlataformaApi.Json))!;
        Assert.Equal(2, v2.Numero);
        Assert.Equal(v1.Itens.Select(i => (i.ItemId, i.Texto)), v2.Itens.Select(i => (i.ItemId, i.Texto)));

        // Corrige o texto do capacete (mesmo item), tira as luvas, inclui um item novo e reordena.
        var capacete = v1.Itens[0].ItemId;
        var edicao = new EdicaoRascunho(
        [
            new ItemRascunho(null, "Protetor auricular"),
            new ItemRascunho(capacete, "Capacete com jugular afivelada"),
            new ItemRascunho(v1.Itens[2].ItemId, v1.Itens[2].Texto),
            new ItemRascunho(v1.Itens[3].ItemId, v1.Itens[3].Texto),
        ]);
        var editado = await cliente.PutAsJsonAsync($"/catalogos/{id}/rascunho", edicao, PlataformaApi.Json);
        Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsync($"/catalogos/{id}/rascunho/publicar", null)).StatusCode);

        var publicada2 = (await cliente.GetFromJsonAsync<Versao>($"/catalogos/{id}/versoes/2", PlataformaApi.Json))!;
        Assert.Equal(["Protetor auricular", "Capacete com jugular afivelada", v1.Itens[2].Texto, v1.Itens[3].Texto], publicada2.Itens.Select(i => i.Texto));
        Assert.Equal(capacete, publicada2.Itens[1].ItemId);
        Assert.DoesNotContain(publicada2.Itens[0].ItemId, v1.Itens.Select(i => i.ItemId));

        // A versão 1 continua exatamente como foi publicada.
        var publicada1 = (await cliente.GetFromJsonAsync<Versao>($"/catalogos/{id}/versoes/1", PlataformaApi.Json))!;
        Assert.Equal(v1.Itens.Select(i => (i.ItemId, i.Ordem, i.Texto)), publicada1.Itens.Select(i => (i.ItemId, i.Ordem, i.Texto)));

        var resumo = (await cliente.GetFromJsonAsync<List<ResumoCatalogo>>("/catalogos", PlataformaApi.Json))!.Single(c => c.Id == id);
        Assert.Equal(2, resumo.Publicada!.Numero);
        Assert.Null(resumo.Rascunho);
    }

    [Fact]
    public async Task Catalogo_vazio_nao_pode_ser_publicado()
    {
        banco.Exigir();
        var (_, _, cliente) = await gestao.EmpresaComGestor();

        var criado = await cliente.PostAsJsonAsync("/catalogos", new DadosCatalogo("Fibra óptica", null), PlataformaApi.Json);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var id = (await criado.Content.ReadFromJsonAsync<DetalheCatalogo>(PlataformaApi.Json))!.Id;

        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync($"/catalogos/{id}/rascunho/publicar", null)).StatusCode);
    }

    [Fact]
    public async Task So_um_rascunho_por_vez_e_descartar_libera_o_numero()
    {
        banco.Exigir();
        var (_, _, cliente) = await gestao.EmpresaComGestor();
        var id = await Importar(cliente, "telecom-mesmo-nivel");

        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsync($"/catalogos/{id}/rascunho", null)).StatusCode);

        await cliente.PostAsync($"/catalogos/{id}/rascunho/publicar", null);
        await cliente.PostAsync($"/catalogos/{id}/rascunho", null);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.DeleteAsync($"/catalogos/{id}/rascunho")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.GetAsync($"/catalogos/{id}/rascunho")).StatusCode);

        var reaberto = (await (await cliente.PostAsync($"/catalogos/{id}/rascunho", null)).Content.ReadFromJsonAsync<Versao>(PlataformaApi.Json))!;
        Assert.Equal(2, reaberto.Numero);
    }

    [Fact]
    public async Task Edicao_recusa_item_de_outro_catalogo_repetido_ou_vazio()
    {
        banco.Exigir();
        var (_, _, cliente) = await gestao.EmpresaComGestor();
        var altura = await Importar(cliente, "telecom-altura");
        var mesmoNivel = await Importar(cliente, "telecom-mesmo-nivel");
        var itemDeAltura = (await cliente.GetFromJsonAsync<Versao>($"/catalogos/{altura}/rascunho", PlataformaApi.Json))!.Itens[1].ItemId;
        var itemDeMesmoNivel = (await cliente.GetFromJsonAsync<Versao>($"/catalogos/{mesmoNivel}/rascunho", PlataformaApi.Json))!.Itens[0].ItemId;

        foreach (var invalida in new EdicaoRascunho[]
        {
            new([new ItemRascunho(itemDeAltura, "Cinto")]),
            new([new ItemRascunho(null, "Luvas"), new ItemRascunho(null, " luvas ")]),
            new([new ItemRascunho(itemDeMesmoNivel, "A"), new ItemRascunho(itemDeMesmoNivel, "B")]),
            new([new ItemRascunho(null, "  ")]),
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync($"/catalogos/{mesmoNivel}/rascunho", invalida, PlataformaApi.Json)).StatusCode);
        }

        // Nada mudou no rascunho depois das tentativas recusadas.
        Assert.Equal(4, (await cliente.GetFromJsonAsync<Versao>($"/catalogos/{mesmoNivel}/rascunho", PlataformaApi.Json))!.Itens.Count);
    }

    // --- Metadados

    [Fact]
    public async Task Renomear_desativar_e_reativar()
    {
        banco.Exigir();
        var (_, _, cliente) = await gestao.EmpresaComGestor();
        var altura = await Importar(cliente, "telecom-altura");
        await Importar(cliente, "telecom-mesmo-nivel");

        Assert.Equal(HttpStatusCode.Conflict,
            (await cliente.PutAsJsonAsync($"/catalogos/{altura}", new DadosCatalogo("Telecom — Mesmo Nível", null), PlataformaApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await cliente.PutAsJsonAsync($"/catalogos/{altura}", new DadosCatalogo("Telecom — Torres", "Só torres."), PlataformaApi.Json)).StatusCode);

        await cliente.PostAsync($"/catalogos/{altura}/desativar", null);
        Assert.DoesNotContain((await cliente.GetFromJsonAsync<List<ResumoCatalogo>>("/catalogos", PlataformaApi.Json))!, c => c.Id == altura);
        var todos = (await cliente.GetFromJsonAsync<List<ResumoCatalogo>>("/catalogos?incluirInativos=true", PlataformaApi.Json))!;
        Assert.Equal("Telecom — Torres", todos.Single(c => c.Id == altura).Nome);

        await cliente.PostAsync($"/catalogos/{altura}/reativar", null);
        Assert.Contains((await cliente.GetFromJsonAsync<List<ResumoCatalogo>>("/catalogos", PlataformaApi.Json))!, c => c.Id == altura);
    }

    [Fact]
    public async Task Mesmo_nome_pode_existir_em_empresas_diferentes()
    {
        banco.Exigir();
        var (_, _, clienteA) = await gestao.EmpresaComGestor();
        var (_, _, clienteB) = await gestao.EmpresaComGestor();

        Assert.Equal(HttpStatusCode.Created, (await clienteA.PostAsync("/catalogos/modelos/telecom-altura", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await clienteB.PostAsync("/catalogos/modelos/telecom-altura", null)).StatusCode);
    }

    // --- Isolamento

    [Fact]
    public async Task Gestor_de_outra_empresa_nao_enxerga_nem_altera_o_catalogo()
    {
        banco.Exigir();
        var (_, _, clienteA) = await gestao.EmpresaComGestor();
        var (_, _, clienteB) = await gestao.EmpresaComGestor();
        var id = await Importar(clienteA, "telecom-altura");

        Assert.Equal(HttpStatusCode.NotFound, (await clienteB.GetAsync($"/catalogos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clienteB.GetAsync($"/catalogos/{id}/rascunho")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clienteB.GetAsync($"/catalogos/{id}/versoes/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clienteB.PutAsJsonAsync($"/catalogos/{id}/rascunho", new EdicaoRascunho([new ItemRascunho(null, "X")]), PlataformaApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clienteB.PostAsync($"/catalogos/{id}/rascunho/publicar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clienteB.DeleteAsync($"/catalogos/{id}/rascunho")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clienteB.PostAsync($"/catalogos/{id}/desativar", null)).StatusCode);
        Assert.Empty((await clienteB.GetFromJsonAsync<List<ResumoCatalogo>>("/catalogos?incluirInativos=true", PlataformaApi.Json))!);

        Assert.Equal("10", await banco.Valor($"SELECT count(*) FROM catalogo_versao_item i JOIN catalogo_versao v ON v.id = i.catalogo_versao_id WHERE v.catalogo_id = '{id}' AND v.publicada_em IS NULL"));
    }

    private static async Task<Guid> Importar(HttpClient cliente, string modelo)
    {
        var resposta = await cliente.PostAsync($"/catalogos/modelos/{modelo}", null);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<DetalheCatalogo>(PlataformaApi.Json))!.Id;
    }
}
