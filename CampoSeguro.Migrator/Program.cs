using CampoSeguro.Data;
using Microsoft.EntityFrameworkCore;

// Único processo que altera o esquema. Roda no deploy, sozinho, antes de subir as APIs;
// nenhum backend aplica migration na inicialização.
//
//   CAMPOSEGURO_DB="Host=...;Database=...;Username=...;Password=..." CampoSeguro.Migrator
//   CampoSeguro.Migrator --listar      (mostra as pendentes sem aplicar)

var conexao = Environment.GetEnvironmentVariable(CampoSeguroDb.VariavelConexao);
if (string.IsNullOrWhiteSpace(conexao))
{
    Console.Error.WriteLine($"Defina a variável de ambiente {CampoSeguroDb.VariavelConexao} com a connection string.");
    return 2;
}

await using var db = CampoSeguroDb.Criar(conexao, ContextoTenantFixo.SemTenant);

var pendentes = (await db.Database.GetPendingMigrationsAsync()).ToList();
Console.WriteLine(pendentes.Count == 0
    ? "Banco atualizado; nenhuma migration pendente."
    : $"Pendentes ({pendentes.Count}):{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", pendentes)}");

if (pendentes.Count == 0 || args.Contains("--listar"))
    return 0;

await db.Database.MigrateAsync();
Console.WriteLine("Migrations aplicadas.");
return 0;
