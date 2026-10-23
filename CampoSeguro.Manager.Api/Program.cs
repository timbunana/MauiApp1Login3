using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CampoSeguro.Data;
using CampoSeguro.Manager.Api;
using CampoSeguro.Manager.Api.Autenticacao;
using CampoSeguro.Manager.Api.Convites;
using CampoSeguro.Manager.Api.Perfil;

// API do Painel de gestão: usada pelo gestor da empresa-cliente. Tudo que ela lê e grava fica preso
// ao tenant do token (ContextoTenantDaRequisicao); a exceção, antes do login, é BuscaSemTenant.cs.
//
// Configuração obrigatória (variáveis de ambiente):
//   CAMPOSEGURO_DB   connection string do PostgreSQL
//   Jwt__Chave       segredo do HS256, pelo menos 32 bytes aleatórios, diferente do da Plataforma

var builder = WebApplication.CreateBuilder(args);

var conexao = builder.Configuration[CampoSeguroDb.VariavelConexao];
if (string.IsNullOrWhiteSpace(conexao))
    throw new InvalidOperationException($"Defina a variável de ambiente {CampoSeguroDb.VariavelConexao} com a connection string.");

var jwt = builder.Configuration.GetSection("Jwt").Get<OpcoesJwt>() ?? new OpcoesJwt();
jwt.Validar();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IContextoTenant, ContextoTenantDaRequisicao>();
builder.Services.AddDbContext<CampoSeguroDbContext>(o => o.Configurar(conexao));
builder.Services.AddScoped<BuscaSemTenant>();

builder.Services.AddAutenticacaoGestao(jwt);

// Login e convite são as rotas abertas. Atrás do Nginx, configurar ForwardedHeaders com o IP do
// proxy, senão o limite passa a valer para todos os clientes juntos.
var tentativas = builder.Configuration.GetValue("Login:TentativasPorMinuto", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(LoginEndpoints.PoliticaLimite, http => RateLimitPartition.GetFixedWindowLimiter(
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

app.MapLogin();
app.MapConvites();
app.MapPerfil();

app.Run();
