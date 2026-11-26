using MsgHatch.Data.EFCore.Providers;
using Npgsql;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Unit;

/// <summary>
/// The PostgreSQL dialect (D144, D318, D322, D323): a missing permission or a failed login is rejected, and a missing
/// table or column or a value that is too long fails the validation, so the dispatcher does not retry them. Each other
/// state and each other exception give null, so the built-in mapping keeps them transient. The claim statement claims the
/// due rows of each channel, or of one channel. The test builds each <see cref="PostgresException"/>, so it needs no server.
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

    [Fact]
    public void ErrorTypeOf_NpgsqlExceptionWithNoState_GivesNull()
    {
        // Arrange
        IEntityFrameworkStoreDialect dialect = PostgreSqlStoreDialect.Instance;
        var exception = new NpgsqlException("The connection failed.");

        // Act
        var type = dialect.ErrorTypeOf(exception);

        // Assert
        Assert.Null(type);
    }

    [Fact]
    public void Dialect_PostgreSql_HasSchemasAndTheJsonTypeAndTheCollationC()
    {
        // Arrange
        IEntityFrameworkStoreDialect dialect = PostgreSqlStoreDialect.Instance;

        // Act
        var facts = (dialect.HasSchemas, dialect.JsonColumnType, dialect.TextCollation);

        // Assert
        Assert.Equal((true, (string?)"json", (string?)"C"), facts);
    }

    [Fact]
    public void ClaimStatement_NoChannel_ClaimsTheDueRowsOfEachChannel()
    {
        // Arrange
        var dialect = PostgreSqlStoreDialect.Instance;
        var names = Names();

        // Act
        var statement = dialect.ClaimStatement(names);

        // Assert
        Assert.Equal(
            """
            UPDATE t AS claimed SET d = due.n, c = @c,
                n = @l, l = @l
            FROM (
                SELECT s, n FROM t
                WHERE st = @p AND n <= @now
                ORDER BY n, s
                LIMIT @m
                FOR UPDATE SKIP LOCKED) AS due
            WHERE claimed.s = due.s
            RETURNING claimed.*
            """,
            statement);
    }

    [Fact]
    public void ClaimStatement_Channel_ClaimsOnlyTheDueRowsOfTheChannel()
    {
        // Arrange
        var dialect = PostgreSqlStoreDialect.Instance;
        var names = Names() with { Channel = "ch", ChannelParameter = "@ch" };

        // Act
        var statement = dialect.ClaimStatement(names);

        // Assert
        Assert.Equal(
            """
            UPDATE t AS claimed SET d = due.n, c = @c,
                n = @l, l = @l
            FROM (
                SELECT s, n FROM t
                WHERE st = @p AND ch = @ch AND n <= @now
                ORDER BY n, s
                LIMIT @m
                FOR UPDATE SKIP LOCKED) AS due
            WHERE claimed.s = due.s
            RETURNING claimed.*
            """,
            statement);
    }

    [Fact]
    public void ClaimStatement_NullNames_ThrowsArgumentNullException()
    {
        // Arrange
        var dialect = PostgreSqlStoreDialect.Instance;

        // Act
        var exception = Record.Exception(() => dialect.ClaimStatement(null!));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    private static ClaimStatementNames Names() => new()
    {
        Table = "t", Sequence = "s", Status = "st", NextTryAtUtc = "n", LeaseUntilUtc = "l", ClaimToken = "c", ClaimedDueAtUtc = "d",
        MaxCountParameter = "@m", NowUtcParameter = "@now", LeaseUntilUtcParameter = "@l", ClaimTokenParameter = "@c", PendingParameter = "@p",
    };
}
