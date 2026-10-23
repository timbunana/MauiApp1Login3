using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CampoSeguro.Data;

public static class ErrosDoBanco
{
    /// <summary>
    /// Índice único recusou a gravação. As APIs conferem a unicidade antes, para dar a mensagem
    /// certa; isto cobre duas requisições simultâneas, quando quem decide é o banco.
    /// </summary>
    public static bool ViolouUnicidade(this DbUpdateException erro) =>
        erro.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
