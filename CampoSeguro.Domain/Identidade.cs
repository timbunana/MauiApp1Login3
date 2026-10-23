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

    /// <param name="convitePendente">Convite de primeiro acesso ainda não usado nem revogado (pode estar vencido).</param>
    public SituacaoAcesso Situacao(Convite? convitePendente, DateTimeOffset agora) =>
        !Ativo ? SituacaoAcesso.Desativado
        : SenhaHash is not null ? SituacaoAcesso.Ativo
        : convitePendente is not null && convitePendente.ExpiraEm > agora ? SituacaoAcesso.ConvitePendente
        : SituacaoAcesso.ConviteExpirado;

    /// <summary>
    /// Apaga os dados pessoais e mantém o registro, que as APRs assinadas citam como autor. Irreversível.
    /// O e-mail vira um endereço inválido e único, para não colidir com ninguém.
    /// </summary>
    public void Anonimizar(DateTimeOffset agora)
    {
        if (Ativo)
            throw new InvalidOperationException("Desative o colaborador antes de anonimizar.");

        Nome = "Colaborador anonimizado";
        Email = EmailNormalizado = $"anonimizado-{Id:N}@anonimizado.invalid";
        SenhaHash = null;
        AnonimizadoEm = agora;
    }
}

/// <summary>Em que pé está o acesso de um colaborador, do ponto de vista de quem o administra.</summary>
public enum SituacaoAcesso
{
    /// <summary>Já definiu a senha.</summary>
    Ativo,

    /// <summary>Tem um link válido e ainda não usado.</summary>
    ConvitePendente,

    /// <summary>Não definiu a senha e não tem link válido: é preciso reenviar o convite.</summary>
    ConviteExpirado,

    Desativado,
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
    /// <summary>
    /// Renovada a cada uso: um técnico que sincroniza ao menos uma vez a cada 90 dias nunca precisa
    /// digitar a senha de novo. Desativar o colaborador ou redefinir a senha revoga antes disso.
    /// </summary>
    public static readonly TimeSpan Validade = TimeSpan.FromDays(90);

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
