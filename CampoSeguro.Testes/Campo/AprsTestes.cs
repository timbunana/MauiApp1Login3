using System.Net;
using System.Net.Http.Json;
using CampoSeguro.Domain;
using CampoSeguro.Field.Api.Aprs;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;

namespace CampoSeguro.Testes.Campo;

public sealed class AprsTestes(BancoDeTeste banco) : IAsyncDisposable
{
    private readonly CampoApi campo = new(banco);

    public ValueTask DisposeAsync() => campo.DisposeAsync();

    // --- Recebimento

    [Fact]
    public async Task Recebe_a_apr_e_aceita_o_reenvio_sem_duplicar()
    {
        var (tenant, tecnico, sessao, cliente, apr) = await Preparar(fotos: 2);

        var primeira = await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(), PlataformaApi.Json);
        var reenvio = await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reenvio.StatusCode);
        var situacao = (await reenvio.Content.ReadFromJsonAsync<SituacaoApr>(PlataformaApi.Json))!;
        Assert.Equal(apr.Fotos.Keys.Order(), situacao.EvidenciasPendentes);
        Assert.True(situacao.PdfPendente);

        Assert.Equal("1", await banco.Valor($"SELECT count(*) FROM apr WHERE id = '{apr.Id}'"));
        Assert.Equal($"{tenant}|{tecnico.Nome}|{sessao.Tecnico.Id}",
            await banco.Valor($"SELECT tenant_id || '|' || autor_nome || '|' || autor_id FROM apr WHERE id = '{apr.Id}'"));
        Assert.Equal("2|2", await banco.Valor($"SELECT (SELECT count(*) FROM apr_item WHERE apr_id = '{apr.Id}') || '|' || (SELECT count(*) FROM apr_evidencia WHERE apr_id = '{apr.Id}')"));
        Assert.Equal("1", await banco.Valor($"SELECT count(*) FROM apr a JOIN sessao_dispositivo s ON s.id = a.sessao_dispositivo_id WHERE a.id = '{apr.Id}'"));
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"/aprs/{apr.Id}")).StatusCode);
    }

    [Fact]
    public async Task Mesmo_id_com_conteudo_diferente_responde_409()
    {
        var (_, _, _, cliente, apr) = await Preparar();
        await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(), PlataformaApi.Json);

        var outra = new AprDeTeste { Id = apr.Id, Autor = apr.Autor, Versao = apr.Versao, Itens = apr.Itens, RespostaPadrao = Resposta.NaoConforme };
        var resposta = await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", outra.Montar(), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("Conforme", await banco.Valor($"SELECT resposta FROM apr_item WHERE apr_id = '{apr.Id}' LIMIT 1"));
    }

    [Fact]
    public async Task Hash_que_nao_confere_e_recusado()
    {
        var (_, _, _, cliente, apr) = await Preparar();

        var resposta = await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(hash: new string('a', 64)), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("0", await banco.Valor($"SELECT count(*) FROM apr WHERE id = '{apr.Id}'"));
    }

    [Fact]
    public async Task Apr_precisa_responder_todos_os_itens_com_o_texto_publicado()
    {
        var (_, _, _, cliente, apr) = await Preparar();
        var faltando = apr.Itens.Take(1).Select(i => new ItemEnviado(i.ItemId, i.Ordem, i.Texto, Resposta.Conforme, null)).ToList();
        var adulterado = apr.Itens.Select(i => new ItemEnviado(i.ItemId, i.Ordem, i.Texto + " (editado)", Resposta.Conforme, null)).ToList();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(faltando), PlataformaApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(adulterado), PlataformaApi.Json)).StatusCode);
    }

    [Fact]
    public async Task Item_sem_resposta_volta_400()
    {
        var (_, _, _, cliente, apr) = await Preparar();
        var enviada = apr.Montar() with { Itens = apr.Itens.Select(i => new ItemEnviado(i.ItemId, i.Ordem, i.Texto, null, null)).ToList() };

        var resposta = await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", enviada, PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Versao_em_rascunho_ou_de_outra_empresa_e_recusada()
    {
        var (tenant, tecnico, _, cliente, apr) = await Preparar();
        var (_, versaoDeOutra, itensDeOutra) = await Pessoas.CatalogoPublicado(banco, await Pessoas.NovaEmpresa(banco), "Capacete");
        var rascunho = Guid.CreateVersion7();
        await banco.Sql($"INSERT INTO catalogo_versao (id, tenant_id, catalogo_id, numero, criado_em) SELECT '{rascunho}', tenant_id, catalogo_id, 2, now() FROM catalogo_versao WHERE id = '{apr.Versao}'");

        var deOutra = new AprDeTeste { Autor = tecnico.Id, Versao = versaoDeOutra, Itens = itensDeOutra };
        var comRascunho = new AprDeTeste { Autor = tecnico.Id, Versao = rascunho, Itens = apr.Itens };

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await cliente.PutAsJsonAsync($"/aprs/{deOutra.Id}", deOutra.Montar(), PlataformaApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await cliente.PutAsJsonAsync($"/aprs/{comRascunho.Id}", comRascunho.Montar(), PlataformaApi.Json)).StatusCode);
        Assert.Equal("0", await banco.Valor($"SELECT count(*) FROM apr WHERE tenant_id = '{tenant}'"));
    }

    [Fact]
    public async Task Id_ja_usado_em_outra_empresa_e_recusado_sem_vazar_nada()
    {
        var (_, _, _, clienteA, aprA) = await Preparar();
        await clienteA.PutAsJsonAsync($"/aprs/{aprA.Id}", aprA.Montar(), PlataformaApi.Json);
        var (_, _, _, clienteB, aprB) = await Preparar();
        var mesmoId = new AprDeTeste { Id = aprA.Id, Autor = aprB.Autor, Versao = aprB.Versao, Itens = aprB.Itens };

        var resposta = await clienteB.PutAsJsonAsync($"/aprs/{aprA.Id}", mesmoId.Montar(), PlataformaApi.Json);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clienteB.GetAsync($"/aprs/{aprA.Id}")).StatusCode);
        Assert.Equal(aprA.Autor.ToString(), await banco.Valor($"SELECT autor_id FROM apr WHERE id = '{aprA.Id}'"));
    }

    // --- Fotos

    [Fact]
    public async Task Foto_sobe_para_a_pasta_da_empresa_e_reenvio_nao_regrava()
    {
        var (tenant, _, _, cliente, apr) = await Preparar(fotos: 1);
        await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(), PlataformaApi.Json);
        var (foto, bytes) = apr.Fotos.Single();
        var rota = $"/aprs/{apr.Id}/evidencias/{foto}";

        var resposta = await cliente.PutAsync(rota, AprDeTeste.Arquivo(bytes, "image/jpeg"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Empty((await resposta.Content.ReadFromJsonAsync<SituacaoApr>(PlataformaApi.Json))!.EvidenciasPendentes);
        var arquivo = Path.Combine(campo.Raiz, $"{tenant:N}", "aprs", $"{apr.Id:N}", "evidencias", $"{foto:N}.jpg");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(arquivo));
        Assert.Equal($"{tenant:N}/aprs/{apr.Id:N}/evidencias/{foto:N}.jpg", await banco.Valor($"SELECT caminho FROM apr_evidencia WHERE id = '{foto}'"));

        Assert.Equal(HttpStatusCode.OK, (await cliente.PutAsync(rota, AprDeTeste.Arquivo(bytes, "image/jpeg"))).StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(campo.Raiz, ".recebendo")));
    }

    [Fact]
    public async Task Foto_diferente_da_registrada_ou_fora_do_formato_e_recusada()
    {
        var (_, _, _, cliente, apr) = await Preparar(fotos: 1);
        await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(), PlataformaApi.Json);
        var (foto, bytes) = apr.Fotos.Single();
        var rota = $"/aprs/{apr.Id}/evidencias/{foto}";
        var trocada = (byte[])bytes.Clone();
        trocada[^1] ^= 0xFF;

        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsync(rota, AprDeTeste.Arquivo(trocada, "image/jpeg"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsync(rota, AprDeTeste.Arquivo([.. bytes, 0x00], "image/jpeg"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await cliente.PutAsync(rota, AprDeTeste.Arquivo(bytes, "image/png"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.PutAsync($"/aprs/{apr.Id}/evidencias/{Guid.NewGuid()}", AprDeTeste.Arquivo(bytes, "image/jpeg"))).StatusCode);

        Assert.Equal("", await banco.Valor($"SELECT coalesce(caminho, '') FROM apr_evidencia WHERE id = '{foto}'"));
        Assert.Empty(Directory.GetFiles(Path.Combine(campo.Raiz, ".recebendo")));
    }

    // --- PDF

    [Fact]
    public async Task Pdf_e_anexado_uma_vez()
    {
        var (_, _, _, cliente, apr) = await Preparar();
        await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(), PlataformaApi.Json);
        var rota = $"/aprs/{apr.Id}/pdf";

        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsync(rota, AprDeTeste.Arquivo("não é pdf"u8.ToArray(), "application/pdf"))).StatusCode);

        var resposta = await cliente.PutAsync(rota, AprDeTeste.Arquivo(AprDeTeste.Pdf, "application/pdf"));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.False((await resposta.Content.ReadFromJsonAsync<SituacaoApr>(PlataformaApi.Json))!.PdfPendente);

        Assert.Equal(HttpStatusCode.OK, (await cliente.PutAsync(rota, AprDeTeste.Arquivo(AprDeTeste.Pdf, "application/pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PutAsync(rota, AprDeTeste.Arquivo([.. AprDeTeste.Pdf, 0x0A], "application/pdf"))).StatusCode);
    }

    // --- Isolamento

    [Fact]
    public async Task Outro_tecnico_nao_consulta_nem_sobe_arquivo_na_apr_alheia()
    {
        var (tenant, _, _, cliente, apr) = await Preparar(fotos: 1);
        await cliente.PutAsJsonAsync($"/aprs/{apr.Id}", apr.Montar(), PlataformaApi.Json);
        var (foto, bytes) = apr.Fotos.Single();
        var (_, _, _, colega) = await campo.TecnicoLogado(tenant);
        var (_, _, _, deOutraEmpresa) = await campo.TecnicoLogado();

        foreach (var outro in new[] { colega, deOutraEmpresa })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await outro.GetAsync($"/aprs/{apr.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outro.PutAsync($"/aprs/{apr.Id}/evidencias/{foto}", AprDeTeste.Arquivo(bytes, "image/jpeg"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outro.PutAsync($"/aprs/{apr.Id}/pdf", AprDeTeste.Arquivo(AprDeTeste.Pdf, "application/pdf"))).StatusCode);
        }
    }

    private async Task<(Guid Tenant, Usuario Tecnico, CampoSeguro.Field.Api.Autenticacao.SessoesEndpoints.Sessao Sessao, HttpClient Cliente, AprDeTeste Apr)> Preparar(int fotos = 0)
    {
        banco.Exigir();
        var (tenant, tecnico, sessao, cliente) = await campo.TecnicoLogado();
        var (_, versao, itens) = await Pessoas.CatalogoPublicado(banco, tenant, "Uso de Capacete com Jugular", "Luvas de Proteção Adequadas");
        var apr = new AprDeTeste { Autor = tecnico.Id, Versao = versao, Itens = itens };
        for (var i = 0; i < fotos; i++)
            apr.ComFoto();
        return (tenant, tecnico, sessao, cliente, apr);
    }
}
