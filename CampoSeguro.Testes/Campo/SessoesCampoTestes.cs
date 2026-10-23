using System.Net;
using System.Net.Http.Json;
using CampoSeguro.Domain;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;
using static CampoSeguro.Field.Api.Autenticacao.SessoesEndpoints;
using static CampoSeguro.Field.Api.Catalogos.CatalogosEndpoints;

namespace CampoSeguro.Testes.Campo;

public sealed class SessoesCampoTestes(BancoDeTeste banco) : IAsyncDisposable
{
    private readonly CampoApi campo = new(banco);

    public ValueTask DisposeAsync() => campo.DisposeAsync();

    [Fact]
    public async Task Tecnico_entra_e_a_sessao_do_aparelho_e_registrada()
    {
        banco.Exigir();
        var empresa = await Pessoas.NovaEmpresa(banco, "Telecom Alfa");
        var tecnico = await Pessoas.NovoUsuario(banco, empresa, Papel.Tecnico);

        var sessao = await campo.Sessao($" {tecnico.Email.ToUpperInvariant()} ");

        Assert.Equal(tecnico.Id, sessao.Tecnico.Id);
        Assert.Equal("Telecom Alfa", sessao.Empresa.Nome);
        Assert.Equal(43, sessao.TokenRenovacao.Length);
        Assert.True(sessao.RenovacaoExpiraEm > DateTimeOffset.UtcNow.AddDays(89));
        Assert.True(sessao.AcessoExpiraEm < DateTimeOffset.UtcNow.AddHours(2));
        Assert.Equal("Moto G (Android 14)", await banco.Valor($"SELECT dispositivo FROM sessao_dispositivo WHERE usuario_id = '{tecnico.Id}'"));
        Assert.Equal(HttpStatusCode.OK, (await campo.Cliente(sessao.TokenAcesso).GetAsync("/catalogos")).StatusCode);
    }

    [Fact]
    public async Task Gestor_e_mandado_para_o_painel_e_senha_errada_da_401()
    {
        banco.Exigir();
        var empresa = await Pessoas.NovaEmpresa(banco);
        var gestor = await Pessoas.NovoUsuario(banco, empresa, Papel.Gestor);
        var tecnico = await Pessoas.NovoUsuario(banco, empresa, Papel.Tecnico);

        Assert.Equal(HttpStatusCode.Forbidden, (await campo.Login(gestor.Email, Pessoas.Senha)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await campo.Login(tecnico.Email, "senha-errada-123")).StatusCode);
        Assert.Equal("0", await banco.Valor($"SELECT count(*) FROM sessao_dispositivo WHERE tenant_id = '{empresa}'"));
    }

    [Fact]
    public async Task Renovacao_nao_rotaciona_e_estende_a_sessao()
    {
        banco.Exigir();
        var (_, tecnico, sessao, _) = await campo.TecnicoLogado();
        await banco.Sql($"UPDATE sessao_dispositivo SET expira_em = now() + interval '1 day' WHERE usuario_id = '{tecnico.Id}'");

        // A mesma renovação duas vezes: a resposta da primeira "se perdeu" e o app tenta de novo.
        var primeira = await campo.Renovar(sessao.TokenRenovacao);
        var segunda = await campo.Renovar(sessao.TokenRenovacao);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var renovada = (await segunda.Content.ReadFromJsonAsync<Sessao>(PlataformaApi.Json))!;
        Assert.Equal(sessao.TokenRenovacao, renovada.TokenRenovacao);
        Assert.True(renovada.RenovacaoExpiraEm > DateTimeOffset.UtcNow.AddDays(89));
        Assert.Equal(HttpStatusCode.OK, (await campo.Cliente(renovada.TokenAcesso).GetAsync("/catalogos")).StatusCode);
    }

    [Fact]
    public async Task Desativar_o_tecnico_corta_acesso_e_renovacao()
    {
        banco.Exigir();
        var (_, tecnico, sessao, cliente) = await campo.TecnicoLogado();

        await banco.Sql($"UPDATE usuario SET ativo = false, desativado_em = now() WHERE id = '{tecnico.Id}'");

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/catalogos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await campo.Renovar(sessao.TokenRenovacao)).StatusCode);
    }

    [Fact]
    public async Task Sair_revoga_so_a_sessao_deste_aparelho()
    {
        banco.Exigir();
        var (_, tecnico, celular, clienteCelular) = await campo.TecnicoLogado();
        var tablet = await campo.Sessao(tecnico.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await clienteCelular.PostAsync("/autenticacao/sair", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await clienteCelular.GetAsync("/catalogos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await campo.Renovar(celular.TokenRenovacao)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await campo.Cliente(tablet.TokenAcesso).GetAsync("/catalogos")).StatusCode);
    }

    [Fact]
    public async Task Sessao_vencida_ou_token_inventado_exigem_novo_login()
    {
        banco.Exigir();
        var (_, tecnico, sessao, _) = await campo.TecnicoLogado();
        await banco.Sql($"UPDATE sessao_dispositivo SET expira_em = now() - interval '1 minute' WHERE usuario_id = '{tecnico.Id}'");

        Assert.Equal(HttpStatusCode.Unauthorized, (await campo.Renovar(sessao.TokenRenovacao)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await campo.Renovar(CampoSeguro.Data.Seguranca.Tokens.Gerar())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await campo.Renovar("")).StatusCode);
    }

    [Fact]
    public async Task App_baixa_so_a_ultima_versao_publicada_dos_catalogos_ativos_da_propria_empresa()
    {
        banco.Exigir();
        var (empresa, _, _, cliente) = await campo.TecnicoLogado();
        var (altura, _, _) = await Pessoas.CatalogoPublicado(banco, empresa, "Capacete", "Cinto");
        var (inativo, _, _) = await Pessoas.CatalogoPublicado(banco, empresa, "Luvas");
        await banco.Sql($"UPDATE catalogo SET ativo = false WHERE id = '{inativo}'");
        await Pessoas.CatalogoPublicado(banco, await Pessoas.NovaEmpresa(banco), "De outra empresa");

        // Versão 2 publicada e versão 3 em rascunho: o app recebe a 2.
        var itemCapacete = await banco.Valor($"SELECT catalogo_item_id FROM catalogo_versao_item WHERE texto = 'Capacete' AND tenant_id = '{empresa}'");
        foreach (var (numero, texto, publicar) in new[] { (2, "Capacete com jugular", true), (3, "Rascunho que não sai", false) })
        {
            var versao = Guid.CreateVersion7();
            await banco.Sql($"INSERT INTO catalogo_versao (id, tenant_id, catalogo_id, numero, criado_em) VALUES ('{versao}', '{empresa}', '{altura}', {numero}, now())");
            await banco.Sql($"INSERT INTO catalogo_versao_item (id, tenant_id, catalogo_versao_id, catalogo_item_id, ordem, texto, criado_em) VALUES (gen_random_uuid(), '{empresa}', '{versao}', '{itemCapacete}', 1, '{texto}', now())");
            if (publicar)
                await banco.Sql($"UPDATE catalogo_versao SET publicada_em = now() WHERE id = '{versao}'");
        }

        var catalogos = (await cliente.GetFromJsonAsync<List<CatalogoPublicado>>("/catalogos", PlataformaApi.Json))!;

        var unico = Assert.Single(catalogos);
        Assert.Equal(altura, unico.CatalogoId);
        Assert.Equal(2, unico.Numero);
        Assert.Equal("Capacete com jugular", Assert.Single(unico.Itens).Texto);
    }
}
