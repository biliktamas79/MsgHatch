using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.Sqlite.Tests.Unit;

public sealed class SqliteStoreOptionsValidatorUnitTests
{
    private const string _connectionString = nameof(SqliteStoreOptions.ConnectionString);

    public static TheoryData<string?, string[]> ConnectionStringRows => new()
    {
        { null, [_connectionString] },
        { "", [_connectionString] },
        { "   ", [_connectionString] },
        { "Data Source=msghatch.db", [] },
        { "Data Source=C:\\Ödön\\üzenetek & <x>.db", [] },
        { "Data Source=data/msghatch-{tenant}.db", [_connectionString] },
    };

    [Theory]
    [MemberData(nameof(ConnectionStringRows))]
    public void Validate_ConnectionString_FailsWhenItIsMissingOrHasTheTenantPlaceholder(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new SqliteStoreOptions { ConnectionString = value! };

        // Act
        var result = new SqliteStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
