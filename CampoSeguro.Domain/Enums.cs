namespace CampoSeguro.Domain;

// Enums são gravados como texto no banco: o valor salvo continua legível em SQL manual
// e não muda de significado se alguém reordenar os membros aqui.

public enum SituacaoAssinatura
{
    Ativa,
    Suspensa,
    Cancelada,
}

public enum Papel
{
    Gestor,
    Tecnico,
}

public enum TipoConvite
{
    /// <summary>Primeiro acesso: o colaborador define a senha.</summary>
    Convite,

    /// <summary>"Esqueci minha senha": mesmo mecanismo, outro texto e outra auditoria.</summary>
    RedefinicaoSenha,
}

public enum Resposta
{
    Conforme,
    NaoConforme,
    NaoAplica,
}
