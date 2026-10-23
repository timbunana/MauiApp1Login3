using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CampoSeguro.Data;
using CampoSeguro.Data.Arquivos;
using CampoSeguro.Field.Api;
using CampoSeguro.Field.Api.Aprs;
using CampoSeguro.Field.Api.Autenticacao;
using CampoSeguro.Field.Api.Catalogos;

// API do app de campo: sessão por aparelho, download do catálogo publicado e sincronização das APRs.
// Tudo fica preso ao tenant do token (ContextoTenantDaRequisicao); a exceção, antes do login, é
// BuscaSemTenant.cs.
//
// Configuração obrigatória (variáveis de ambiente):
//   CAMPOSEGURO_DB          connection string do PostgreSQL
//   Jwt__Chave              segredo do HS256, pelo menos 32 bytes, diferente dos outros backends
//   Armazenamento__Raiz     pasta das fotos e PDFs (com backup, como o banco)

var builder = WebApplication.CreateBuilder(args);

var conexao = builder.Configuration[CampoSeguroDb.VariavelConexao];
if (string.IsNullOrWhiteSpace(conexao))
    throw new InvalidOperationException($"Defina a variável de ambiente {CampoSeguroDb.VariavelConexao} com a connection string.");

var jwt = builder.Configuration.GetSection("Jwt").Get<OpcoesJwt>() ?? new OpcoesJwt();
jwt.Validar();

builder.Services.AddSingleton(new Armazenamento(builder.Configuration["Armazenamento:Raiz"] ?? ""));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ContextoTenantDaRequisicao>();
builder.Services.AddScoped<IContextoTenant>(sp => sp.GetRequiredService<ContextoTenantDaRequisicao>());
builder.Services.AddDbContext<CampoSeguroDbContext>(o => o.Configurar(conexao));
builder.Services.AddScoped<BuscaSemTenant>();

builder.Services.AddAutenticacaoCampo(jwt);

// Login e renovação são as rotas abertas. Atrás do Nginx, configurar ForwardedHeaders; e lembrar que
// técnicos na mesma rede móvel podem sair pelo mesmo IP (CGNAT), daí o limite mais folgado.
var tentativas = builder.Configuration.GetValue("Login:TentativasPorMinuto", 30);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(SessoesEndpoints.PoliticaLimite, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = tentativas, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

app.MapSessoes();
app.MapCatalogos();
app.MapAprs();

app.Run();
