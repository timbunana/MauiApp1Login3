using System.Net;
using System.Net.Http.Json;
using CampoSeguro.Domain;
using CampoSeguro.Field.Api.Aprs;
using CampoSeguro.Manager.Api.Aprs;
using CampoSeguro.Testes.Campo;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;

namespace CampoSeguro.Testes.Gestao;

/// <summary>APRs enviadas de verdade pela Field.Api e consultadas pelo Painel, sobre a mesma pasta de arquivos.</summary>
public sealed class AprsGestaoTestes : IAsyncDisposable
{
    private readonly BancoDeTeste banco;
    private readonly CampoApi campo;
    private readonly GestaoApi gestao;

    public AprsGestaoTestes(BancoDeTeste banco)
    {
        this.banco = banco;
        campo = new CampoApi(banco);
        gestao = new GestaoApi(banco, campo.Raiz);
    }

    public async ValueTask DisposeAsync()
    {
        await gestao.DisposeAsync();
        await campo.DisposeAsync();
    }

    [Fact]
    public async Task Lista_com_contagens_filtros_e_paginacao()
    {
        var c = await Preparar();

        var todas = (await c.Painel.GetFromJsonAsync<PaginaDeAprs>("/aprs", PlataformaApi.Json))!;
        Assert.Equal(2, todas.Total);
        Assert.Equal([c.ComProblema.Id, c.Tranquila.Id], todas.Aprs.Select(a => a.Id));
        var comProblema = todas.Aprs[0];
        Assert.Equal((3, 1, 0, 1, 1, true), (comProblema.Itens, comProblema.NaoConformes, comProblema.NaoAplica, comProblema.Fotos, comProblema.FotosRecebidas, comProblema.PdfDisponivel));
        Assert.Equal(c.Tecnico.Nome, comProblema.Autor.Nome);
        Assert.Equal(1, comProblema.Catalogo.Versao);

        var naoConformes = (await c.Painel.GetFromJsonAsync<PaginaDeAprs>("/aprs?somenteNaoConformes=true", PlataformaApi.Json))!;
        Assert.Equal([c.ComProblema.Id], naoConformes.Aprs.Select(a => a.Id));

        var dePeriodo = (await c.Painel.GetFromJsonAsync<PaginaDeAprs>(
            $"/aprs?de={Uri.EscapeDataString(c.Tranquila.AssinadaEm.AddMinutes(1).ToString("O"))}", PlataformaApi.Json))!;
        Assert.Equal([c.ComProblema.Id], dePeriodo.Aprs.Select(a => a.Id));

        Assert.Equal(2, (await c.Painel.GetFromJsonAsync<PaginaDeAprs>($"/aprs?tecnicoId={c.Tecnico.Id}", PlataformaApi.Json))!.Total);
        Assert.Equal(0, (await c.Painel.GetFromJsonAsync<PaginaDeAprs>($"/aprs?tecnicoId={Guid.NewGuid()}", PlataformaApi.Json))!.Total);

        var pagina2 = (await c.Painel.GetFromJsonAsync<PaginaDeAprs>("/aprs?pagina=2&tamanho=1", PlataformaApi.Json))!;
        Assert.Equal((2, 2, 1), (pagina2.Total, pagina2.Pagina, pagina2.Tamanho));
        Assert.Equal([c.Tranquila.Id], pagina2.Aprs.Select(a => a.Id));
    }

    [Fact]
    public async Task Detalhe_traz_itens_fotos_hash_e_aparelho()
    {
        var c = await Preparar();

        var detalhe = (await c.Painel.GetFromJsonAsync<DetalheApr>($"/aprs/{c.ComProblema.Id}", PlataformaApi.Json))!;

        Assert.Equal(c.ItensPublicados.Select(i => i.Texto), detalhe.Itens.Select(i => i.Texto));
        Assert.Equal(Resposta.NaoConforme, detalhe.Itens[1].Resposta);
        Assert.Equal("Sem luvas no estoque.", detalhe.Itens[1].Observacao);
        Assert.Equal(SituacaoArquivo.Disponivel, Assert.Single(detalhe.Fotos).Situacao);
        Assert.Equal(c.EnviadaComProblema.ConteudoHash, detalhe.ConteudoHash);
        Assert.Equal("Moto G (Android 14)", detalhe.Dispositivo);
        Assert.Equal(-23.55052, detalhe.Local.Latitude);
    }

    [Fact]
    public async Task Pdf_e_foto_saem_identicos_ao_enviado_e_sem_cache()
    {
        var c = await Preparar();
        var (fotoId, bytes) = c.ComProblema.Fotos.Single();

        var pdf = await c.Painel.GetAsync($"/aprs/{c.ComProblema.Id}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.Equal(AprDeTeste.Pdf, await pdf.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", pdf.Headers.CacheControl!.ToString());

        var foto = await c.Painel.GetAsync($"/aprs/{c.ComProblema.Id}/evidencias/{fotoId}");
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        Assert.Equal(bytes, await foto.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Arquivo_ainda_nao_sincronizado_ou_expurgado()
    {
        var c = await Preparar(subirFoto: false);
        var fotoId = c.ComProblema.Fotos.Keys.Single();

        Assert.Equal(HttpStatusCode.NotFound, (await c.Painel.GetAsync($"/aprs/{c.ComProblema.Id}/evidencias/{fotoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.Painel.GetAsync($"/aprs/{c.Tranquila.Id}/pdf")).StatusCode);

        await banco.Sql($"UPDATE apr_evidencia SET recebida_em = now(), expurgada_em = now() WHERE id = '{fotoId}'");
        Assert.Equal(HttpStatusCode.Gone, (await c.Painel.GetAsync($"/aprs/{c.ComProblema.Id}/evidencias/{fotoId}")).StatusCode);
    }

    [Fact]
    public async Task Arquivo_alterado_ou_sumido_do_disco_nao_e_entregue()
    {
        var c = await Preparar();
        var (fotoId, _) = c.ComProblema.Fotos.Single();
        var caminhoPdf = Path.Combine(campo.Raiz, $"{c.Empresa:N}", "aprs", $"{c.ComProblema.Id:N}", "apr.pdf");
        var caminhoFoto = Path.Combine(campo.Raiz, $"{c.Empresa:N}", "aprs", $"{c.ComProblema.Id:N}", "evidencias", $"{fotoId:N}.jpg");

        await File.WriteAllBytesAsync(caminhoPdf, [.. AprDeTeste.Pdf, (byte)'X']);
        File.Delete(caminhoFoto);

        Assert.Equal(HttpStatusCode.InternalServerError, (await c.Painel.GetAsync($"/aprs/{c.ComProblema.Id}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await c.Painel.GetAsync($"/aprs/{c.ComProblema.Id}/evidencias/{fotoId}")).StatusCode);
    }

    [Fact]
    public async Task Gestor_de_outra_empresa_nao_ve_nada()
    {
        var c = await Preparar();
        var (_, _, outroPainel) = await gestao.EmpresaComGestor();
        var (fotoId, _) = c.ComProblema.Fotos.Single();

        Assert.Equal(0, (await outroPainel.GetFromJsonAsync<PaginaDeAprs>("/aprs", PlataformaApi.Json))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await outroPainel.GetAsync($"/aprs/{c.ComProblema.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outroPainel.GetAsync($"/aprs/{c.ComProblema.Id}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outroPainel.GetAsync($"/aprs/{c.ComProblema.Id}/evidencias/{fotoId}")).StatusCode);
    }

    [Fact]
    public async Task Autor_anonimizado_continua_assinando_as_aprs_antigas()
    {
        var c = await Preparar();

        await c.Painel.PostAsync($"/colaboradores/{c.Tecnico.Id}/desativar", null);
        await c.Painel.PostAsync($"/colaboradores/{c.Tecnico.Id}/anonimizar", null);

        var lista = (await c.Painel.GetFromJsonAsync<PaginaDeAprs>("/aprs", PlataformaApi.Json))!;
        Assert.All(lista.Aprs, a => Assert.Equal(c.Tecnico.Nome, a.Autor.Nome));
    }

    private sealed record Contexto(
        Guid Empresa, Usuario Tecnico, HttpClient Painel, IReadOnlyList<Pessoas.ItemPublicado> ItensPublicados,
        AprDeTeste Tranquila, AprDeTeste ComProblema, AprEnviada EnviadaComProblema);

    /// <summary>Duas APRs do mesmo técnico: uma toda conforme, sem arquivos; outra com luvas não conforme, foto e PDF.</summary>
    private async Task<Contexto> Preparar(bool subirFoto = true)
    {
        banco.Exigir();
        var (empresa, tecnico, _, app) = await campo.TecnicoLogado();
        var gestor = await Pessoas.NovoUsuario(banco, empresa, Papel.Gestor);
        var painel = await gestao.ClienteAutenticado(gestor.Email, Pessoas.Senha);
        var (_, versao, itens) = await Pessoas.CatalogoPublicado(banco, empresa, "Capacete", "Luvas", "Bota");

        var tranquila = new AprDeTeste { Autor = tecnico.Id, Versao = versao, Itens = itens, AssinadaEm = DateTimeOffset.UtcNow.AddHours(-3) };
        Assert.Equal(HttpStatusCode.Created, (await app.PutAsJsonAsync($"/aprs/{tranquila.Id}", tranquila.Montar(), PlataformaApi.Json)).StatusCode);

        var comProblema = new AprDeTeste { Autor = tecnico.Id, Versao = versao, Itens = itens, AssinadaEm = DateTimeOffset.UtcNow.AddHours(-1) }.ComFoto();
        var enviada = comProblema.Montar(itens.Select(i => new ItemEnviado(i.ItemId, i.Ordem, i.Texto,
            i.Texto == "Luvas" ? Resposta.NaoConforme : Resposta.Conforme, i.Texto == "Luvas" ? "Sem luvas no estoque." : null)).ToList());
        Assert.Equal(HttpStatusCode.Created, (await app.PutAsJsonAsync($"/aprs/{comProblema.Id}", enviada, PlataformaApi.Json)).StatusCode);
        if (subirFoto)
        {
            var (fotoId, bytes) = comProblema.Fotos.Single();
            Assert.Equal(HttpStatusCode.OK, (await app.PutAsync($"/aprs/{comProblema.Id}/evidencias/{fotoId}", AprDeTeste.Arquivo(bytes, "image/jpeg"))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsync($"/aprs/{comProblema.Id}/pdf", AprDeTeste.Arquivo(AprDeTeste.Pdf, "application/pdf"))).StatusCode);

        return new Contexto(empresa, tecnico, painel, itens, tranquila, comProblema, enviada);
    }
}
