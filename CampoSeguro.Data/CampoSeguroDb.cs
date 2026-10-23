using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CampoSeguro.Data;

public static class CampoSeguroDb
{
    /// <summary>Variável de ambiente lida pelo Migrator e pelas ferramentas do EF.</summary>
    public const string VariavelConexao = "CAMPOSEGURO_DB";

    /// <summary>Configuração única do provedor; todo backend e o Migrator passam por aqui.</summary>
    /// <remarks>Não genérico porque o <c>AddDbContext</c> dos backends entrega o builder sem o tipo.</remarks>
    public static DbContextOptionsBuilder Configurar(this DbContextOptionsBuilder opcoes, string conexao) =>
        opcoes
            .UseNpgsql(conexao, npgsql => npgsql.MigrationsHistoryTable("ef_historico_migracoes"))
            .UseSnakeCaseNamingConvention();

    public static CampoSeguroDbContext Criar(string conexao, IContextoTenant contexto)
    {
        var opcoes = new DbContextOptionsBuilder<CampoSeguroDbContext>();
        opcoes.Configurar(conexao);
        return new(opcoes.Options, contexto);
    }
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
