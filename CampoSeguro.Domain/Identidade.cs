namespace CampoSeguro.Domain;

/// <summary>Empresa-cliente. Raiz do isolamento: tudo de negócio aponta para um Tenant.</summary>
public class Tenant : Entidade
{
    public required string Nome { get; set; }

    /// <summary>CNPJ só com dígitos.</summary>
    public required string Documento { get; set; }

    public SituacaoAssinatura SituacaoAssinatura { get; set; } = SituacaoAssinatura.Ativa;
}

/// <summary>
/// Membro da equipe CampoSeguro. Fica fora de <see cref="Usuario"/> de propósito: não pertence a
/// nenhum tenant, enxerga todos e só autentica na Plataforma. Misturar os dois exigiria TenantId
/// anulável em Usuario, o que impede as chaves compostas que garantem o isolamento.
/// </summary>
public class AdministradorPlataforma : Entidade
{
    public required string Nome { get; set; }
    public required string Email { get; set; }
    public required string EmailNormalizado { get; set; }
    public required string SenhaHash { get; set; }
    public bool Ativo { get; set; } = true;
}

/// <summary>Gestor ou técnico de uma empresa-cliente.</summary>
public class Usuario : EntidadeDoTenant
{
    public required string Nome { get; set; }
    public required string Email { get; set; }

    /// <summary>E-mail em minúsculas e sem espaços; é por ele que o login busca e a unicidade vale.</summary>
    public required string EmailNormalizado { get; set; }

    /// <summary>Nulo até o colaborador aceitar o convite e definir a senha.</summary>
    public string? SenhaHash { get; set; }

    public Papel Papel { get; set; }

    /// <summary>Desativar nunca exclui: as APRs assinadas precisam manter o autor.</summary>
    public bool Ativo { get; set; } = true;

    public DateTimeOffset? DesativadoEm { get; set; }

    /// <summary>LGPD: nome e e-mail sobrescritos. A APR preserva o nome congelado do autor.</summary>
    public DateTimeOffset? AnonimizadoEm { get; set; }
}

/// <summary>Link de convite ou de redefinição de senha. Só o hash do token fica no banco.</summary>
public class Convite : EntidadeDoTenant
{
    /// <summary>Prazo do link. Curto o bastante para limitar o risco, longo o bastante para cobrir um fim de semana.</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromHours(72);

    public Guid UsuarioId { get; set; }
    public TipoConvite Tipo { get; set; }

    /// <summary>SHA-256 do token enviado no link (32 bytes).</summary>
    public required byte[] TokenHash { get; set; }

    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? UsadoEm { get; set; }

    /// <summary>Preenchido quando um reenvio substitui este convite.</summary>
    public DateTimeOffset? RevogadoEm { get; set; }

    public Usuario? Usuario { get; set; }
}

/// <summary>
/// Refresh token de um aparelho. Existe para que desativar um colaborador corte o acesso:
/// sem esta tabela, um token de vida longa continuaria sincronizando.
/// </summary>
public class SessaoDispositivo : EntidadeDoTenant
{
    public Guid UsuarioId { get; set; }

    /// <summary>SHA-256 do refresh token (32 bytes).</summary>
    public required byte[] TokenHash { get; set; }

    /// <summary>Descrição livre do aparelho (modelo, sistema), para o gestor reconhecer a sessão.</summary>
    public string? Dispositivo { get; set; }

    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? UltimoUsoEm { get; set; }
    public DateTimeOffset? RevogadaEm { get; set; }

    public Usuario? Usuario { get; set; }
}
