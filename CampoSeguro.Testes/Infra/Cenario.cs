using CampoSeguro.Domain;

namespace CampoSeguro.Testes.Infra;

/// <summary>
/// Duas empresas com um técnico cada. A empresa A tem o catálogo "Telecom — Altura" com a versão 1
/// publicada (capacete e cinto) e uma APR assinada com o item do capacete.
/// </summary>
public sealed class Cenario
{
    public const string TextoCapacete = "Capacete com jugular";
    public const string TextoCinto = "Cinto paraquedista";

    public Guid TenantA { get; } = Guid.CreateVersion7();
    public Guid TenantB { get; } = Guid.CreateVersion7();
    public Guid TecnicoA { get; } = Guid.CreateVersion7();
    public Guid TecnicoB { get; } = Guid.CreateVersion7();
    public string EmailTecnicoA { get; } = Gerar.Email("tecnico-a");
    public Guid Catalogo { get; } = Guid.CreateVersion7();
    public Guid ItemCapacete { get; } = Guid.CreateVersion7();
    public Guid ItemCinto { get; } = Guid.CreateVersion7();
    public Guid Versao { get; } = Guid.CreateVersion7();
    public Guid Apr { get; } = Guid.CreateVersion7();

    public static async Task<Cenario> Criar(BancoDeTeste banco)
    {
        banco.Exigir();
        var c = new Cenario();

        await using (var db = banco.Contexto(null))
        {
            db.Tenants.AddRange(
                new Tenant { Id = c.TenantA, Nome = "Empresa A", Documento = Gerar.Cnpj() },
                new Tenant { Id = c.TenantB, Nome = "Empresa B", Documento = Gerar.Cnpj() });
            db.Usuarios.AddRange(
                Tecnico(c.TecnicoA, c.TenantA, "Tec A", c.EmailTecnicoA),
                Tecnico(c.TecnicoB, c.TenantB, "Tec B", Gerar.Email("tecnico-b")));
            db.Catalogos.Add(new Catalogo { Id = c.Catalogo, TenantId = c.TenantA, Nome = "Telecom — Altura" });
            db.CatalogoItens.AddRange(
                new CatalogoItem { Id = c.ItemCapacete, TenantId = c.TenantA, CatalogoId = c.Catalogo },
                new CatalogoItem { Id = c.ItemCinto, TenantId = c.TenantA, CatalogoId = c.Catalogo });
            db.CatalogoVersoes.Add(new CatalogoVersao { Id = c.Versao, TenantId = c.TenantA, CatalogoId = c.Catalogo, Numero = 1 });
            db.CatalogoVersaoItens.AddRange(
                new CatalogoVersaoItem { TenantId = c.TenantA, CatalogoVersaoId = c.Versao, CatalogoItemId = c.ItemCapacete, Ordem = 1, Texto = TextoCapacete },
                new CatalogoVersaoItem { TenantId = c.TenantA, CatalogoVersaoId = c.Versao, CatalogoItemId = c.ItemCinto, Ordem = 2, Texto = TextoCinto });
            await db.SaveChangesAsync();
        }

        // Publicar é um UPDATE à parte: o trigger recusa incluir itens numa versão já publicada.
        await banco.Sql($"UPDATE catalogo_versao SET publicada_em = now() WHERE id = '{c.Versao}'");

        await using (var db = banco.Contexto(c.TenantA, c.TecnicoA))
        {
            db.Aprs.Add(new Apr
            {
                Id = c.Apr,
                AutorId = c.TecnicoA,
                AutorNome = "Tec A",
                CatalogoVersaoId = c.Versao,
                AssinadaEm = DateTimeOffset.UtcNow,
                RecebidaEm = DateTimeOffset.UtcNow,
                ConteudoHash = new byte[32],
                Latitude = -23.55,
                Longitude = -46.63,
                LocalDescricao = "Poste 1234",
                Itens = [new AprItem { CatalogoItemId = c.ItemCapacete, Ordem = 1, Texto = TextoCapacete, Resposta = Resposta.Conforme }],
            });
            await db.SaveChangesAsync();
        }

        return c;
    }

    private static Usuario Tecnico(Guid id, Guid tenant, string nome, string email) => new()
    {
        Id = id,
        TenantId = tenant,
        Nome = nome,
        Email = email,
        EmailNormalizado = Emails.Normalizar(email),
        Papel = Papel.Tecnico,
    };

    /// <summary>INSERT de APR em SQL puro, com as colunas obrigatórias e o que o teste quiser trocar.</summary>
    public string InsertApr(Guid? tenant = null, Guid? autor = null, Guid? versao = null, string hash = "", string extraColunas = "", string extraValores = "") =>
        $"""
        INSERT INTO apr (id, tenant_id, autor_id, autor_nome, catalogo_versao_id, assinada_em, recebida_em, conteudo_hash, criado_em{extraColunas})
        VALUES (gen_random_uuid(), '{tenant ?? TenantA}', '{autor ?? TecnicoA}', 'Tec', '{versao ?? Versao}', now(), now(),
                {(hash == "" ? Gerar.Hash(0xab) : hash)}, now(){extraValores})
        """;

    public string InsertAprItem(Guid item, string texto, string resposta = "Conforme", int ordem = 2) =>
        $"""
        INSERT INTO apr_item (id, tenant_id, apr_id, catalogo_item_id, ordem, texto, resposta, criado_em)
        VALUES (gen_random_uuid(), '{TenantA}', '{Apr}', '{item}', {ordem}, '{texto}', '{resposta}', now())
        """;
}
