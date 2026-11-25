using System.Reflection;
using MsgHatch.Data.EFCore.Providers;
using MySqlConnector;

namespace MsgHatch.Data.EFCore.MySql.Tests.Integration;

/// <summary>
/// The error types of the MySQL dialect (D322): a missing permission or a failed login is rejected, and a missing table or
/// column or a value that is too long fails the validation, so the dispatcher does not retry them. Each other number gives
/// null, so the built-in mapping keeps it transient. MySqlConnector has no public constructor of <see cref="MySqlException"/>,
/// so the test calls its non-public constructor, and needs no server.
/// </summary>
public sealed class MySqlStoreDialectIntegTests
{
    public static TheoryData<string, MySqlErrorCode, int?> ErrorRows => new()
    {
        { "database access denied", MySqlErrorCode.DatabaseAccessDenied, 403 },
        { "access denied", MySqlErrorCode.AccessDenied, 403 },
        { "table access denied", MySqlErrorCode.TableAccessDenied, 403 },
        { "column access denied", MySqlErrorCode.ColumnAccessDenied, 403 },
        { "bad field", MySqlErrorCode.BadFieldError, 400 },
        { "no such table", MySqlErrorCode.NoSuchTable, 400 },
        { "data too long", MySqlErrorCode.DataTooLong, 400 },
        { "deadlock", MySqlErrorCode.LockDeadlock, null },
    };

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public void ErrorTypeOf_MySqlException_GivesTheErrorTypeOfItsNumber(string row, MySqlErrorCode number, int? expected)
    {
        // Arrange
        IEntityFrameworkStoreDialect dialect = MySqlStoreDialect.Instance;
        var exception = (MySqlException)Activator.CreateInstance(
            typeof(MySqlException), BindingFlags.Instance | BindingFlags.NonPublic, binder: null, [number, "The message of the server."], culture: null)!;

        // Act
        var type = dialect.ErrorTypeOf(exception);

        // Assert
        Assert.Equal((row, expected), (row, type?.Code));
    }

    [Fact]
    public void Dialect_MySql_HasNoSchemasAndNoJsonTypeAndTheBinaryCollation()
    {
        // Arrange
        IEntityFrameworkStoreDialect dialect = MySqlStoreDialect.Instance;

        // Act
        var facts = (dialect.HasSchemas, dialect.JsonColumnType, dialect.TextCollation, dialect.ClaimStatement(AnyNames()));

        // Assert
        Assert.Equal((false, (string?)null, (string?)"utf8mb4_bin", (string?)null), facts);
    }

    private static ClaimStatementNames AnyNames() => new()
    {
        Table = "t", Sequence = "s", Status = "st", NextTryAtUtc = "n", LeaseUntilUtc = "l", ClaimToken = "c", ClaimedDueAtUtc = "d",
        MaxCountParameter = "@m", NowUtcParameter = "@now", LeaseUntilUtcParameter = "@l", ClaimTokenParameter = "@c", PendingParameter = "@p",
    };
}
