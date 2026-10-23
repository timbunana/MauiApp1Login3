using CampoSeguro.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

[assembly: AssemblyFixture(typeof(CampoSeguro.Testes.Infra.BancoDeTeste))]

namespace CampoSeguro.Testes.Infra;

/// <summary>
/// Um banco novo por execução: criado com as migrations reais (inclusive os triggers) e apagado no fim.
/// Os testes não limpam dados entre si; cada um cria as próprias empresas com CNPJ e e-mail aleatórios.
/// Sem <see cref="Variavel"/>, os testes que usam banco são pulados, e os de domínio rodam normalmente.
/// </summary>
public sealed class BancoDeTeste : IAsyncLifetime
{
    /// <summary>Conexão com um servidor PostgreSQL de teste, sem <c>Database</c>. Nunca aponte para produção.</summary>
    public const string Variavel = "CAMPOSEGURO_TESTE_DB";

    private readonly string? servidor = Environment.GetEnvironmentVariable(Variavel);
    private string? nome;

    public string Conexao { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(servidor))
            return;

        nome = $"camposeguro_teste_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 10000)}";
        await Executar(servidor, $"CREATE DATABASE {nome}");
        Conexao = new NpgsqlConnectionStringBuilder(servidor) { Database = nome }.ConnectionString;

        await using var db = Contexto(null);
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (nome is null)
            return;

        NpgsqlConnection.ClearAllPools();
        await Executar(servidor!, $"DROP DATABASE IF EXISTS {nome} WITH (FORCE)");
    }

    /// <summary>Primeira linha de todo teste que usa banco.</summary>
    public void Exigir() =>
        Assert.SkipWhen(nome is null, $"Defina {Variavel} (ex.: Host=localhost;Port=45432;Username=postgres) para rodar os testes com banco.");

    public CampoSeguroDbContext Contexto(Guid? tenant, Guid? usuario = null) =>
        CampoSeguroDb.Criar(Conexao, new ContextoTenantFixo(tenant, usuario));

    /// <summary>SQL direto, sem passar pelo EF: é assim que se prova que a regra está no banco.</summary>
    public async Task<string?> Valor(string sql)
    {
        await using var conexao = new NpgsqlConnection(Conexao);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        return (await comando.ExecuteScalarAsync())?.ToString();
    }

    public Task Sql(string sql) => Executar(Conexao, sql);

    /// <summary>Executa um comando que o banco deve recusar e devolve o SQLSTATE do erro.</summary>
    public async Task<string> Recusado(string sql)
    {
        var erro = await Assert.ThrowsAsync<PostgresException>(() => Executar(Conexao, sql));
        return erro.SqlState;
    }

    private static async Task Executar(string conexaoTexto, string sql)
    {
        await using var conexao = new NpgsqlConnection(conexaoTexto);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync();
    }
}
