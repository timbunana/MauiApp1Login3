using CampoSeguro.Domain;
using CampoSeguro.Testes.Infra;
using Microsoft.EntityFrameworkCore;

namespace CampoSeguro.Testes.Banco;

/// <summary>Filtro global de tenant e conferência de gravação no <c>CampoSeguroDbContext</c>.</summary>
public sealed class IsolamentoTenantTestes(BancoDeTeste banco)
{
    [Fact]
    public async Task Cada_empresa_ve_so_os_proprios_registros()
    {
        var c = await Cenario.Criar(banco);

        await using var a = banco.Contexto(c.TenantA);
        Assert.Equal([c.TecnicoA], await a.Usuarios.Select(u => u.Id).ToListAsync());

        await using var b = banco.Contexto(c.TenantB);
        Assert.Equal([c.TecnicoB], await b.Usuarios.Select(u => u.Id).ToListAsync());
        Assert.Empty(await b.Aprs.ToListAsync());
        Assert.Empty(await b.AprItens.ToListAsync());
        Assert.Empty(await b.Catalogos.ToListAsync());
    }

    [Fact]
    public async Task Busca_por_id_de_outra_empresa_devolve_nulo()
    {
        var c = await Cenario.Criar(banco);

        await using var b = banco.Contexto(c.TenantB);
        Assert.Null(await b.Aprs.FindAsync(c.Apr));
    }

    [Fact]
    public async Task Sem_tenant_no_contexto_nada_e_visivel_e_IgnoreQueryFilters_ve_tudo()
    {
        var c = await Cenario.Criar(banco);

        await using var s = banco.Contexto(null);
        Assert.False(await s.Usuarios.AnyAsync());
        Assert.False(await s.Catalogos.AnyAsync());

        var todos = await s.Usuarios.IgnoreQueryFilters().Select(u => u.Id).ToListAsync();
        Assert.Contains(c.TecnicoA, todos);
        Assert.Contains(c.TecnicoB, todos);
    }

    [Fact]
    public async Task Gravar_registro_de_outra_empresa_e_recusado()
    {
        var c = await Cenario.Criar(banco);

        await using var a = banco.Contexto(c.TenantA);
        a.Catalogos.Add(new Catalogo { Nome = "Invasão", TenantId = c.TenantB });
        await Assert.ThrowsAsync<InvalidOperationException>(() => a.SaveChangesAsync());
    }

    [Fact]
    public async Task Registro_novo_herda_tenant_e_autor_e_grava_em_utc()
    {
        var c = await Cenario.Criar(banco);
        var horarioLocal = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(-3));
        var catalogo = new Catalogo { Nome = "Telecom — Mesmo Nível", CriadoEm = horarioLocal };

        await using (var a = banco.Contexto(c.TenantA, c.TecnicoA))
        {
            a.Catalogos.Add(catalogo);
            await a.SaveChangesAsync();
        }

        await using var leitura = banco.Contexto(c.TenantA);
        var gravado = await leitura.Catalogos.SingleAsync(x => x.Id == catalogo.Id);
        Assert.Equal(c.TenantA, gravado.TenantId);
        Assert.Equal(c.TecnicoA, gravado.CriadoPor);
        Assert.Equal(TimeSpan.Zero, gravado.CriadoEm.Offset);
        Assert.Equal(horarioLocal.UtcDateTime, gravado.CriadoEm.UtcDateTime);
        Assert.Equal(7, gravado.Id.Version);
    }

    [Fact]
    public async Task TenantId_de_registro_existente_nao_muda()
    {
        var c = await Cenario.Criar(banco);

        await using var a = banco.Contexto(c.TenantA);
        var catalogo = await a.Catalogos.SingleAsync(x => x.Id == c.Catalogo);
        catalogo.TenantId = c.TenantB;
        await Assert.ThrowsAsync<InvalidOperationException>(() => a.SaveChangesAsync());
    }

    [Fact]
    public async Task Sem_tenant_no_contexto_o_TenantId_precisa_ser_explicito()
    {
        banco.Exigir();

        await using var s = banco.Contexto(null);
        s.Catalogos.Add(new Catalogo { Nome = "Sem dono" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.SaveChangesAsync());
    }
}
