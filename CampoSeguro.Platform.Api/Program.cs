using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CampoSeguro.Data;
using CampoSeguro.Platform.Api.Autenticacao;
using CampoSeguro.Platform.Api.Empresas;

// API da equipe CampoSeguro: cadastra as empresas-clientes e convida o primeiro gestor de cada uma.
// É o único backend que enxerga todos os tenants — ver TodosOsTenants.cs.
//
// Configuração obrigatória (variáveis de ambiente):
//   CAMPOSEGURO_DB        connection string do PostgreSQL
//   Jwt__Chave            segredo do HS256, pelo menos 32 bytes aleatórios
//   Convites__UrlAceite   página do Painel onde o gestor define a senha

var builder = WebApplication.CreateBuilder(args);

var conexao = builder.Configuration[CampoSeguroDb.VariavelConexao];
if (string.IsNullOrWhiteSpace(conexao))
    throw new InvalidOperationException($"Defina a variável de ambiente {CampoSeguroDb.VariavelConexao} com a connection string.");

var jwt = builder.Configuration.GetSection("Jwt").Get<OpcoesJwt>() ?? new OpcoesJwt();
jwt.Validar();

var convites = builder.Configuration.GetSection("Convites").Get<OpcoesConvite>() ?? new OpcoesConvite();
convites.Validar();
builder.Services.AddSingleton(convites);

// A Plataforma nunca tem tenant no contexto. Gravações informam o TenantId explicitamente, e as
// leituras de dados de tenant passam por TodosOsTenants. Migrations ficam a cargo do Migrator.
builder.Services.AddSingleton<IContextoTenant>(ContextoTenantFixo.SemTenant);
builder.Services.AddDbContext<CampoSeguroDbContext>(o => o.Configurar(conexao));

builder.Services.AddAutenticacaoPlataforma(jwt);

// Atrás do Nginx todo login chega do mesmo IP e o limite passa a ser global — aceitável para uma
// equipe pequena. Quando a infra existir, configurar ForwardedHeaders com o IP do proxy.
var tentativasLogin = builder.Configuration.GetValue("Login:TentativasPorMinuto", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(LoginEndpoints.PoliticaLimite, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = tentativasLogin, Window = TimeSpan.FromMinutes(1) }));
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

app.MapLogin();
app.MapEmpresas();

app.Run();
