using CampoSeguro.Data;
using CampoSeguro.Data.Seguranca;
using CampoSeguro.Domain;
using Microsoft.EntityFrameworkCore;

// Único processo que altera o esquema. Roda no deploy, sozinho, antes de subir as APIs;
// nenhum backend aplica migration na inicialização.
//
//   CAMPOSEGURO_DB="Host=...;Database=...;Username=...;Password=..." CampoSeguro.Migrator
//   CampoSeguro.Migrator --listar      (mostra as pendentes sem aplicar)
//   CampoSeguro.Migrator --criar-admin "Nome Completo" email@camposeguro.com.br
//
// --criar-admin é a única forma de criar um administrador da Plataforma: a API não tem
// cadastro aberto, e o primeiro administrador não teria quem o convidasse. A senha é lida do
// terminal sem eco ou, em scripts, da variável CAMPOSEGURO_SENHA_ADMIN — nunca de argumento,
// que fica no histórico do shell e na lista de processos.

var conexao = Environment.GetEnvironmentVariable(CampoSeguroDb.VariavelConexao);
if (string.IsNullOrWhiteSpace(conexao))
{
    Console.Error.WriteLine($"Defina a variável de ambiente {CampoSeguroDb.VariavelConexao} com a connection string.");
    return 2;
}

await using var db = CampoSeguroDb.Criar(conexao, ContextoTenantFixo.SemTenant);

var pendentes = (await db.Database.GetPendingMigrationsAsync()).ToList();

if (args is ["--criar-admin", ..])
    return await CriarAdministrador(db, args[1..], pendentes.Count);

Console.WriteLine(pendentes.Count == 0
    ? "Banco atualizado; nenhuma migration pendente."
    : $"Pendentes ({pendentes.Count}):{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", pendentes)}");

if (pendentes.Count == 0 || args.Contains("--listar"))
    return 0;

await db.Database.MigrateAsync();
Console.WriteLine("Migrations aplicadas.");
return 0;

static async Task<int> CriarAdministrador(CampoSeguroDbContext db, string[] args, int pendentes)
{
    if (args is not [var nome, var email] || string.IsNullOrWhiteSpace(nome))
    {
        Console.Error.WriteLine("Uso: --criar-admin \"Nome Completo\" email@dominio");
        return 2;
    }

    if (pendentes > 0)
    {
        Console.Error.WriteLine("Há migrations pendentes. Rode o Migrator sem argumentos antes de criar o administrador.");
        return 1;
    }

    if (!Emails.Valido(email))
    {
        Console.Error.WriteLine($"E-mail inválido: {email}");
        return 2;
    }

    var normalizado = Emails.Normalizar(email);
    if (await db.AdministradoresPlataforma.AnyAsync(a => a.EmailNormalizado == normalizado))
    {
        Console.Error.WriteLine($"Já existe um administrador com o e-mail {normalizado}.");
        return 1;
    }

    var senha = Environment.GetEnvironmentVariable("CAMPOSEGURO_SENHA_ADMIN") ?? LerSenhaDoTerminal();
    if (senha is null)
    {
        Console.Error.WriteLine("Sem terminal interativo: defina CAMPOSEGURO_SENHA_ADMIN.");
        return 2;
    }

    if (Senhas.Problema(senha) is { } problema)
    {
        Console.Error.WriteLine(problema);
        return 2;
    }

    db.AdministradoresPlataforma.Add(new AdministradorPlataforma
    {
        Nome = nome.Trim(),
        Email = email.Trim(),
        EmailNormalizado = normalizado,
        SenhaHash = Senhas.Gerar(senha),
    });
    await db.SaveChangesAsync();

    Console.WriteLine($"Administrador {normalizado} criado.");
    return 0;
}

static string? LerSenhaDoTerminal()
{
    if (Console.IsInputRedirected)
        return null;

    var primeira = LerSemEco("Senha: ");
    var segunda = LerSemEco("Repita a senha: ");
    if (primeira == segunda)
        return primeira;

    Console.Error.WriteLine("As senhas não conferem.");
    Environment.Exit(2);
    return null;
}

static string LerSemEco(string rotulo)
{
    Console.Write(rotulo);
    var senha = new System.Text.StringBuilder();
    while (Console.ReadKey(intercept: true) is var tecla && tecla.Key != ConsoleKey.Enter)
    {
        if (tecla.Key == ConsoleKey.Backspace)
        {
            if (senha.Length > 0)
                senha.Length--;
        }
        else if (!char.IsControl(tecla.KeyChar))
        {
            senha.Append(tecla.KeyChar);
        }
    }

    Console.WriteLine();
    return senha.ToString();
}
