using CampoSeguro.Domain;

namespace CampoSeguro.Manager.Api.Colaboradores;

/// <summary>Sem <see cref="Papel"/>, o colaborador é técnico.</summary>
public sealed record NovoColaborador(string? Nome, string? Email, Papel? Papel);

public sealed record Colaborador(
    Guid Id, string Nome, string Email, Papel Papel, SituacaoAcesso Situacao,
    DateTimeOffset? ConviteExpiraEm, DateTimeOffset CriadoEm, DateTimeOffset? DesativadoEm, bool Anonimizado);

/// <summary>O link carrega o token em claro e só existe nesta resposta. O gestor o repassa ao colaborador.</summary>
public sealed record ConviteEmitido(string Link, DateTimeOffset ExpiraEm);

public sealed record ColaboradorConvidado(Colaborador Colaborador, ConviteEmitido Convite);

/// <summary>Um aparelho em que o técnico entrou no app.</summary>
public sealed record SessaoDoAparelho(
    Guid Id, string? Dispositivo, DateTimeOffset CriadaEm, DateTimeOffset? UltimoUsoEm, DateTimeOffset ExpiraEm, bool Ativa);
