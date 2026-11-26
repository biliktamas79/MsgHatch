using MsgHatch.Data.EFCore.Providers;
using Npgsql;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Unit;

/// <summary>
/// The error types of the PostgreSQL dialect (D322): a missing permission or a failed login is rejected, and a missing
/// table or column or a value that is too long fails the validation, so the dispatcher does not retry them. Each other
/// state gives null, so the built-in mapping keeps it transient. The test builds each <see cref="PostgresException"/>, so it
/// needs no server.
/// </summary>
public sealed class PostgreSqlStoreDialectUnitTests
{
    public static TheoryData<string, string, int?> ErrorRows => new()
    {
        { "insufficient privilege", "42501", 403 },
        { "invalid authorization specification", "28000", 403 },
        { "invalid password", "28P01", 403 },
        { "undefined table", "42P01", 400 },
        { "undefined column", "42703", 400 },
        { "string data right truncation", "22001", 400 },
        { "serialization failure", "40001", null },
    };

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public void ErrorTypeOf_PostgresException_GivesTheErrorTypeOfItsState(string row, string state, int? expected)
    {
        // Arrange
        IEntityFrameworkStoreDialect dialect = PostgreSqlStoreDialect.Instance;
        var exception = new PostgresException("The message of the server.", "ERROR", "ERROR", state);

        // Act
        var type = dialect.ErrorTypeOf(exception);

        // Assert
        Assert.Equal((row, expected), (row, type?.Code));
    }
}
