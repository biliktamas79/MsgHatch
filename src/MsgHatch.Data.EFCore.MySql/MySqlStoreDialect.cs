using System.Data.Common;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Domain.Failures;
using MySqlConnector;

namespace MsgHatch.Data.EFCore.MySql;

/// <summary>
/// The dialect of the EF Core stores on MySQL (D318, D325, D326). The default collation is <c>utf8mb4_bin</c>, so an id or
/// a name is case-sensitive and sorts in binary order. MariaDB also has this collation. MySQL has no
/// <c>UPDATE ... RETURNING</c>, so the dialect gives no claim statement, and the claim of the base package runs. A schema of
/// MySQL is a database, so the tables have the prefix <c>MsgHatch</c> and no schema. Each JSON column keeps the text type
/// of the provider, because the MySQL type <c>json</c> sorts the keys and drops duplicate keys, so a host selects it only
/// with the option (D323). A missing permission or a failed login is rejected, and a missing table or column or a value
/// that is too long fails the validation, so the dispatcher does not retry them (D322).
/// </summary>
public sealed class MySqlStoreDialect : IEntityFrameworkStoreDialect
{
    private MySqlStoreDialect()
    {
    }

    /// <summary>The one instance of the dialect.</summary>
    public static MySqlStoreDialect Instance { get; } = new();

    /// <inheritdoc />
    public string ProviderName => "Microting.EntityFrameworkCore.MySql";

    /// <inheritdoc />
    public string? TextCollation => "utf8mb4_bin";

    /// <inheritdoc />
    public bool HasSchemas => false;

    /// <inheritdoc />
    public string? ClaimStatement(ClaimStatementNames names) => null;

    /// <inheritdoc />
    public ErrorType? ErrorTypeOf(DbException exception) => (exception as MySqlException)?.ErrorCode switch
    {
        MySqlErrorCode.DatabaseAccessDenied or MySqlErrorCode.AccessDenied or MySqlErrorCode.TableAccessDenied or MySqlErrorCode.ColumnAccessDenied =>
            Errors.Rejected,
        MySqlErrorCode.BadFieldError or MySqlErrorCode.NoSuchTable or MySqlErrorCode.DataTooLong => Errors.ValidationFailed,
        _ => null,
    };
}
