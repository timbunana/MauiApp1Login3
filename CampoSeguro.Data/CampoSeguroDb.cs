using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CampoSeguro.Data;

public static class CampoSeguroDb
{
    /// <summary>Variável de ambiente lida pelo Migrator e pelas ferramentas do EF.</summary>
    public const string VariavelConexao = "CAMPOSEGURO_DB";

    /// <summary>Configuração única do provedor; todo backend e o Migrator passam por aqui.</summary>
    public static DbContextOptionsBuilder<CampoSeguroDbContext> Configurar(
        this DbContextOptionsBuilder<CampoSeguroDbContext> opcoes, string conexao) =>
        opcoes
            .UseNpgsql(conexao, npgsql => npgsql.MigrationsHistoryTable("ef_historico_migracoes"))
            .UseSnakeCaseNamingConvention();

    public static CampoSeguroDbContext Criar(string conexao, IContextoTenant contexto) =>
        new(new DbContextOptionsBuilder<CampoSeguroDbContext>().Configurar(conexao).Options, contexto);
}

/// <summary>
/// Usada por <c>dotnet ef</c>. Gerar migrations não precisa de banco; a conexão só importa
/// para <c>database update</c>, que fora do desenvolvimento local é papel do Migrator.
/// </summary>
internal sealed class FabricaDeDesign : IDesignTimeDbContextFactory<CampoSeguroDbContext>
{
    public CampoSeguroDbContext CreateDbContext(string[] args) =>
        CampoSeguroDb.Criar(
            Environment.GetEnvironmentVariable(CampoSeguroDb.VariavelConexao)
                ?? "Host=localhost;Database=camposeguro_dev",
            ContextoTenantFixo.SemTenant);
}
