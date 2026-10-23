using CampoSeguro.Testes.Infra;
using Npgsql;

namespace CampoSeguro.Testes.Banco;

/// <summary>
/// Regras que o banco garante sozinho (CHECK, índices únicos, FKs compostas e os triggers da
/// migration Imutabilidade). Tudo em SQL puro: se passasse pelo EF, não provaria que um backend
/// com bug ou um SQL manual também seriam barrados.
/// </summary>
public sealed class RestricoesTestes(BancoDeTeste banco)
{
    private const string Restricao = PostgresErrorCodes.RestrictViolation;
    private const string Check = PostgresErrorCodes.CheckViolation;
    private const string Unica = PostgresErrorCodes.UniqueViolation;
    private const string ChaveEstrangeira = PostgresErrorCodes.ForeignKeyViolation;

    // --- Catálogo

    [Fact]
    public async Task Publicar_versao_sem_itens_e_recusado()
    {
        var c = await Cenario.Criar(banco);
        var vazia = Guid.CreateVersion7();
        await banco.Sql($"INSERT INTO catalogo_versao (id, tenant_id, catalogo_id, numero, criado_em) VALUES ('{vazia}', '{c.TenantA}', '{c.Catalogo}', 2, now())");

        Assert.Equal(Check, await banco.Recusado($"UPDATE catalogo_versao SET publicada_em = now() WHERE id = '{vazia}'"));
    }

    [Fact]
    public async Task Apr_com_versao_em_rascunho_e_recusada()
    {
        var c = await Cenario.Criar(banco);
        var rascunho = await NovoRascunho(c);

        Assert.Equal(Check, await banco.Recusado(c.InsertApr(versao: rascunho)));
    }

    [Fact]
    public async Task Versao_publicada_e_imutavel()
    {
        var c = await Cenario.Criar(banco);

        Assert.Equal(Restricao, await banco.Recusado(
            $"INSERT INTO catalogo_versao_item (id, tenant_id, catalogo_versao_id, catalogo_item_id, ordem, texto, criado_em) VALUES (gen_random_uuid(), '{c.TenantA}', '{c.Versao}', '{c.ItemCinto}', 3, 'x', now())"));
        Assert.Equal(Restricao, await banco.Recusado($"UPDATE catalogo_versao_item SET texto = 'outro' WHERE catalogo_versao_id = '{c.Versao}'"));
        Assert.Equal(Restricao, await banco.Recusado($"DELETE FROM catalogo_versao_item WHERE catalogo_versao_id = '{c.Versao}'"));
        Assert.Equal(Restricao, await banco.Recusado($"UPDATE catalogo_versao SET publicada_em = NULL WHERE id = '{c.Versao}'"));
        Assert.Equal(Restricao, await banco.Recusado($"DELETE FROM catalogo_versao WHERE id = '{c.Versao}'"));
    }

    [Fact]
    public async Task Catalogo_aceita_um_rascunho_por_vez()
    {
        var c = await Cenario.Criar(banco);
        await NovoRascunho(c);

        Assert.Equal(Unica, await banco.Recusado(
            $"INSERT INTO catalogo_versao (id, tenant_id, catalogo_id, numero, criado_em) VALUES (gen_random_uuid(), '{c.TenantA}', '{c.Catalogo}', 3, now())"));
    }

    // --- APR

    [Fact]
    public async Task Apr_nao_cruza_empresas()
    {
        var c = await Cenario.Criar(banco);

        // A FK composta (tenant_id, x_id) barra até um GUID forjado vindo do celular.
        Assert.Equal(ChaveEstrangeira, await banco.Recusado(c.InsertApr(tenant: c.TenantB, autor: c.TecnicoB)));
        Assert.Equal(ChaveEstrangeira, await banco.Recusado(c.InsertApr(autor: c.TecnicoB)));
    }

    [Fact]
    public async Task Apr_confere_hash_e_coordenadas()
    {
        var c = await Cenario.Criar(banco);

        Assert.Equal(Check, await banco.Recusado(c.InsertApr(hash: "decode('ab', 'hex')")));
        Assert.Equal(Check, await banco.Recusado(c.InsertApr(extraColunas: ", latitude", extraValores: ", -23.5")));
    }

    [Fact]
    public async Task Item_da_apr_precisa_ser_copia_fiel_da_versao()
    {
        var c = await Cenario.Criar(banco);

        Assert.Equal(Check, await banco.Recusado(c.InsertAprItem(c.ItemCinto, "Cinto adulterado")));
        Assert.Equal(Check, await banco.Recusado(c.InsertAprItem(c.ItemCinto, Cenario.TextoCinto, resposta: "Talvez")));

        await banco.Sql(c.InsertAprItem(c.ItemCinto, Cenario.TextoCinto, resposta: "NaoConforme"));
    }

    [Fact]
    public async Task Apr_assinada_e_imutavel()
    {
        var c = await Cenario.Criar(banco);

        Assert.Equal(Restricao, await banco.Recusado($"UPDATE apr_item SET resposta = 'NaoConforme' WHERE apr_id = '{c.Apr}'"));
        Assert.Equal(Restricao, await banco.Recusado($"DELETE FROM apr_item WHERE apr_id = '{c.Apr}'"));
        Assert.Equal(Restricao, await banco.Recusado($"UPDATE apr SET observacao = 'editado' WHERE id = '{c.Apr}'"));
        Assert.Equal(Restricao, await banco.Recusado($"DELETE FROM apr WHERE id = '{c.Apr}'"));
    }

    [Fact]
    public async Task Pdf_e_anexado_uma_vez_so()
    {
        var c = await Cenario.Criar(banco);

        await banco.Sql($"UPDATE apr SET pdf_caminho = 'a/apr.pdf', pdf_hash = {Gerar.Hash(0xcd)} WHERE id = '{c.Apr}'");

        Assert.Equal(Restricao, await banco.Recusado($"UPDATE apr SET pdf_hash = {Gerar.Hash(0xef)} WHERE id = '{c.Apr}'"));
    }

    // --- Evidência

    [Fact]
    public async Task Evidencia_sobe_depois_e_pode_ser_expurgada_mas_nao_trocada()
    {
        var c = await Cenario.Criar(banco);
        var foto = Guid.CreateVersion7();
        await banco.Sql($"""
            INSERT INTO apr_evidencia (id, tenant_id, apr_id, hash, tamanho_bytes, largura, altura, capturada_em, criado_em)
            VALUES ('{foto}', '{c.TenantA}', '{c.Apr}', {Gerar.Hash(0x01)}, 300000, 1280, 960, now(), now())
            """);

        await banco.Sql($"UPDATE apr_evidencia SET caminho = 'a/foto.jpg', recebida_em = now() WHERE id = '{foto}'");
        Assert.Equal(Restricao, await banco.Recusado($"UPDATE apr_evidencia SET hash = {Gerar.Hash(0x02)} WHERE id = '{foto}'"));
        Assert.Equal(Restricao, await banco.Recusado($"DELETE FROM apr_evidencia WHERE id = '{foto}'"));

        await banco.Sql($"UPDATE apr_evidencia SET caminho = NULL, expurgada_em = now() WHERE id = '{foto}'");
        Assert.Equal("1", await banco.Valor($"SELECT count(*) FROM apr_evidencia WHERE id = '{foto}' AND expurgada_em IS NOT NULL"));
    }

    [Fact]
    public async Task Evidencia_com_caminho_exige_recebida_em()
    {
        var c = await Cenario.Criar(banco);

        Assert.Equal(Check, await banco.Recusado($"""
            INSERT INTO apr_evidencia (id, tenant_id, apr_id, hash, tamanho_bytes, largura, altura, capturada_em, caminho, criado_em)
            VALUES (gen_random_uuid(), '{c.TenantA}', '{c.Apr}', {Gerar.Hash(0x03)}, 1, 1, 1, now(), 'x', now())
            """));
    }

    // --- Identidade

    [Fact]
    public async Task Email_e_unico_entre_ativos_e_liberado_ao_desativar()
    {
        var c = await Cenario.Criar(banco);
        var email = c.EmailTecnicoA.ToLowerInvariant();
        var inserirNaB = $"INSERT INTO usuario (id, tenant_id, nome, email, email_normalizado, papel, ativo, criado_em) VALUES (gen_random_uuid(), '{c.TenantB}', 'X', '{email}', '{email}', 'Tecnico', true, now())";

        Assert.Equal(Unica, await banco.Recusado(inserirNaB));
        Assert.Equal(Check, await banco.Recusado($"UPDATE usuario SET ativo = false WHERE id = '{c.TecnicoA}'"));

        await banco.Sql($"UPDATE usuario SET ativo = false, desativado_em = now() WHERE id = '{c.TecnicoA}'");
        await banco.Sql(inserirNaB);
    }

    [Fact]
    public async Task Convite_tem_um_pendente_por_usuario_e_tipo()
    {
        var c = await Cenario.Criar(banco);
        string Inserir(byte hash) => $"INSERT INTO convite (id, tenant_id, usuario_id, tipo, token_hash, expira_em, criado_em) VALUES (gen_random_uuid(), '{c.TenantB}', '{c.TecnicoB}', 'Convite', {Gerar.Hash(hash)}, now() + interval '72 hours', now())";

        await banco.Sql(Inserir(0xaa));
        Assert.Equal(Unica, await banco.Recusado(Inserir(0xbb)));

        await banco.Sql($"UPDATE convite SET revogado_em = now() WHERE usuario_id = '{c.TecnicoB}'");
        await banco.Sql(Inserir(0xbb));
    }

    [Fact]
    public async Task Documento_da_empresa_so_aceita_14_digitos()
    {
        banco.Exigir();

        Assert.Equal(Check, await banco.Recusado(
            $"INSERT INTO tenant (id, nome, documento, situacao_assinatura, criado_em) VALUES (gen_random_uuid(), 'C', '12ABC34501DE35', 'Ativa', now())"));
    }

    private async Task<Guid> NovoRascunho(Cenario c)
    {
        var id = Guid.CreateVersion7();
        await banco.Sql($"INSERT INTO catalogo_versao (id, tenant_id, catalogo_id, numero, criado_em) VALUES ('{id}', '{c.TenantA}', '{c.Catalogo}', 2, now())");
        await banco.Sql($"INSERT INTO catalogo_versao_item (id, tenant_id, catalogo_versao_id, catalogo_item_id, ordem, texto, criado_em) VALUES (gen_random_uuid(), '{c.TenantA}', '{id}', '{c.ItemCapacete}', 1, 'Capacete', now())");
        return id;
    }
}
