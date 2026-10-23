using CampoSeguro.Domain;

namespace CampoSeguro.Platform.Api.Empresas;

// Os campos de entrada são anuláveis de propósito: um JSON incompleto chega até a validação e
// volta como 400 com a lista de erros, em vez de falhar na desserialização sem dizer o campo.

public sealed record NovoGestor(string? Nome, string? Email);

public sealed record NovaEmpresa(string? Nome, string? Documento, NovoGestor? Gestor);

public sealed record ResumoEmpresa(Guid Id, string Nome, string Documento, SituacaoAssinatura SituacaoAssinatura, DateTimeOffset CriadaEm)
{
    public static ResumoEmpresa De(Tenant t) => new(t.Id, t.Nome, t.Documento, t.SituacaoAssinatura, t.CriadoEm);
}

public sealed record DetalheEmpresa(
    Guid Id, string Nome, string Documento, SituacaoAssinatura SituacaoAssinatura, DateTimeOffset CriadaEm,
    IReadOnlyList<ResumoGestor> Gestores);

public enum SituacaoGestor
{
    /// <summary>Já definiu a senha.</summary>
    Ativo,

    /// <summary>Tem um link válido e ainda não usado.</summary>
    ConvitePendente,

    /// <summary>Não definiu a senha e não tem link válido: é preciso reenviar o convite.</summary>
    ConviteExpirado,

    Desativado,
}

public sealed record ResumoGestor(Guid Id, string Nome, string Email, SituacaoGestor Situacao, DateTimeOffset? ConviteExpiraEm);

/// <summary>O link carrega o token em claro. Ele só existe nesta resposta: o banco guarda o hash.</summary>
public sealed record ConviteEmitido(string Link, DateTimeOffset ExpiraEm);

public sealed record EmpresaCriada(ResumoEmpresa Empresa, ResumoGestor Gestor, ConviteEmitido Convite);

public sealed record GestorConvidado(ResumoGestor Gestor, ConviteEmitido Convite);
