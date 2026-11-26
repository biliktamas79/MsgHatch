using System.Data.Common;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.SqlServer.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Unit;

/// <summary>
/// The SQL Server dialect (D144, D318, D322): a missing permission or a failed login is rejected, and a missing table or
/// column or a value that is too long fails the validation, so the dispatcher does not retry them. Each other number and
/// each other exception give null, so the built-in mapping keeps them transient. The claim statement claims the due rows
/// of each channel, or of one channel. The test builds each <c>SqlException</c>, so it needs no server.
/// </summary>
public sealed class SqlServerStoreDialectUnitTests
{
    public static TheoryData<string, int, int?> ErrorRows => new()
    {
        { "invalid column name", 207, 400 },
        { "invalid object name", 208, 400 },
        { "object permission denied", 229, 403 },
        { "column permission denied", 230, 403 },
        { "database permission denied", 262, 403 },
        { "value truncated in column", 2628, 400 },
        { "value truncated", 8152, 400 },
        { "login failed", 18456, 403 },
        { "deadlock", 1205, null },
    };

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public void ErrorTypeOf_SqlException_GivesTheErrorTypeOfItsNumber(string row, int number, int? expected)
    {
        // Arrange
        IEntityFrameworkStoreDialect dialect = SqlServerStoreDialect.Instance;
        var exception = TestSqlExceptions.ASqlException(number);

        // Act
        var type = dialect.ErrorTypeOf(exception);

        // Assert
        Assert.Equal((row, expected), (row, type?.Code));
    }

    [Fact]
    public void ErrorTypeOf_ExceptionOfAnotherProvider_GivesNull()
    {
        // Arrange
        IEntityFrameworkStoreDialect dialect = SqlServerStoreDialect.Instance;
        var exception = Substitute.For<DbException>();

        // Act
        var type = dialect.ErrorTypeOf(exception);

        // Assert
        Assert.Null(type);
    }

    [Fact]
    public void Dialect_SqlServer_HasSchemasAndNoJsonTypeAndTheBinaryCollation()
    {
        // Arrange
        IEntityFrameworkStoreDialect dialect = SqlServerStoreDialect.Instance;

        // Act
        var facts = (dialect.HasSchemas, dialect.JsonColumnType, dialect.TextCollation);

        // Assert
        Assert.Equal((true, (string?)null, (string?)"Latin1_General_100_BIN2"), facts);
    }

    [Fact]
    public void ClaimStatement_NoChannel_ClaimsTheDueRowsOfEachChannel()
    {
        // Arrange
        var dialect = SqlServerStoreDialect.Instance;
        var names = Names();

        // Act
        var statement = dialect.ClaimStatement(names);

        // Assert
        Assert.Equal(
            """
            UPDATE t SET d = n, c = @c,
                n = @l, l = @l
            OUTPUT inserted.*
            WHERE s IN (
                SELECT TOP (@m) due.s FROM t AS due WITH (ROWLOCK, READPAST, UPDLOCK)
                WHERE due.st = @p AND due.n <= @now
                ORDER BY due.n, due.s)
            """,
            statement);
    }

    [Fact]
    public void ClaimStatement_Channel_ClaimsOnlyTheDueRowsOfTheChannel()
    {
        // Arrange
        var dialect = SqlServerStoreDialect.Instance;
        var names = Names() with { Channel = "ch", ChannelParameter = "@ch" };

        // Act
        var statement = dialect.ClaimStatement(names);

        // Assert
        Assert.Equal(
            """
            UPDATE t SET d = n, c = @c,
                n = @l, l = @l
            OUTPUT inserted.*
            WHERE s IN (
                SELECT TOP (@m) due.s FROM t AS due WITH (ROWLOCK, READPAST, UPDLOCK)
                WHERE due.st = @p AND due.ch = @ch AND due.n <= @now
                ORDER BY due.n, due.s)
            """,
            statement);
    }

    [Fact]
    public void ClaimStatement_NullNames_ThrowsArgumentNullException()
    {
        // Arrange
        var dialect = SqlServerStoreDialect.Instance;

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
