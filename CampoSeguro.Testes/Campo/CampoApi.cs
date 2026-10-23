using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CampoSeguro.Data;
using CampoSeguro.Domain;
using CampoSeguro.Field.Api.Aprs;
using CampoSeguro.Testes.Infra;
using CampoSeguro.Testes.Plataforma;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static CampoSeguro.Field.Api.Autenticacao.SessoesEndpoints;

namespace CampoSeguro.Testes.Campo;

/// <summary>A Field.Api inteira em memória, com uma pasta de arquivos própria, apagada no fim.</summary>
public sealed class CampoApi(BancoDeTeste banco) : WebApplicationFactory<CampoSeguro.Field.Api.Autenticacao.OpcoesJwt>
{
    public string Raiz { get; } = Path.Combine(Path.GetTempPath(), "camposeguro-testes", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting(CampoSeguroDb.VariavelConexao, banco.Conexao);
        builder.UseSetting("Jwt:Chave", "chave-de-teste-so-do-app-de-campo-0123456789");
        builder.UseSetting("Armazenamento:Raiz", Raiz);
        builder.UseSetting("Login:TentativasPorMinuto", "1000");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(Raiz))
            Directory.Delete(Raiz, recursive: true);
    }

    public Task<HttpResponseMessage> Login(string email, string senha, string? dispositivo = "Moto G (Android 14)") =>
        CreateClient().PostAsJsonAsync("/autenticacao/login", new { email, senha, dispositivo }, PlataformaApi.Json);

    public Task<HttpResponseMessage> Renovar(string tokenRenovacao) =>
        CreateClient().PostAsJsonAsync("/autenticacao/renovar", new { tokenRenovacao }, PlataformaApi.Json);

    public async Task<Sessao> Sessao(string email, string senha = Pessoas.Senha)
    {
        var resposta = await Login(email, senha);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<Sessao>(PlataformaApi.Json))!;
    }

    public HttpClient Cliente(string tokenAcesso)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenAcesso);
        return cliente;
    }

    /// <summary>Técnico com senha numa empresa nova, já logado no app.</summary>
    public async Task<(Guid Empresa, Usuario Tecnico, Sessao Sessao, HttpClient Cliente)> TecnicoLogado(Guid? empresa = null)
    {
        var tenant = empresa ?? await Pessoas.NovaEmpresa(banco);
        var tecnico = await Pessoas.NovoUsuario(banco, tenant, Papel.Tecnico);
        var sessao = await Sessao(tecnico.Email);
        return (tenant, tecnico, sessao, Cliente(sessao.TokenAcesso));
    }
}

/// <summary>Monta a APR como o app vai montar: respostas, fotos e o hash do <see cref="ConteudoApr"/>.</summary>
public sealed class AprDeTeste
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid Autor { get; init; }
    public required Guid Versao { get; init; }
    public required IReadOnlyList<Pessoas.ItemPublicado> Itens { get; init; }
    public DateTimeOffset AssinadaEm { get; init; } = new(2026, 10, 23, 8, 15, 30, 123, TimeSpan.FromHours(-3));
    public Resposta RespostaPadrao { get; init; } = Resposta.Conforme;
    public string? Observacao { get; init; } = "Poste com fiação baixa; área isolada.";
    public Dictionary<Guid, byte[]> Fotos { get; } = [];

    public AprDeTeste ComFoto(int tamanho = 2048)
    {
        var bytes = RandomNumberGenerator.GetBytes(tamanho);
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF;
        Fotos[Guid.CreateVersion7()] = bytes;
        return this;
    }

    /// <param name="hash">Para simular um aparelho que calculou errado.</param>
    public AprEnviada Montar(IReadOnlyList<ItemEnviado>? itens = null, string? hash = null)
    {
        itens ??= Itens.Select(i => new ItemEnviado(i.ItemId, i.Ordem, i.Texto, RespostaPadrao, null)).ToList();
        var evidencias = Fotos.Select(f => new EvidenciaEnviada(
            f.Key, Convert.ToHexStringLower(SHA256.HashData(f.Value)), f.Value.Length, 1280, 960, AssinadaEm.AddMinutes(-5))).ToList();

        var conteudo = new ConteudoApr.Conteudo(
            Id, Autor, Versao, AssinadaEm, -23.55052, -46.633308, 8.5, "Poste 1234 — Rua das Flores", Observacao,
            itens.Select(i => new ConteudoApr.Item(i.CatalogoItemId!.Value, i.Ordem!.Value, i.Texto!, i.Resposta!.Value, i.Observacao)).ToList(),
            evidencias.Select(e => new ConteudoApr.Evidencia(e.Id!.Value, e.Hash!)).ToList());

        return new AprEnviada(Versao, AssinadaEm, hash ?? Convert.ToHexStringLower(ConteudoApr.Hash(conteudo)),
            -23.55052, -46.633308, 8.5, "Poste 1234 — Rua das Flores", Observacao, itens, evidencias);
    }

    public static ByteArrayContent Arquivo(byte[] bytes, string tipo)
    {
        var conteudo = new ByteArrayContent(bytes);
        conteudo.Headers.ContentType = new MediaTypeHeaderValue(tipo);
        return conteudo;
    }

    public static readonly byte[] Pdf = "%PDF-1.7\n% APR de teste\n%%EOF\n"u8.ToArray();
}
