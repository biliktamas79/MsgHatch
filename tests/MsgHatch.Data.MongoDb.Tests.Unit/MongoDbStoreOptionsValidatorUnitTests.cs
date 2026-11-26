using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.MongoDb.Tests.Unit;

/// <summary>The options tests of <see cref="MongoDbStoreOptions"/>. They need no database.</summary>
public sealed class MongoDbStoreOptionsValidatorUnitTests
{
    private const string _connectionString = nameof(MongoDbStoreOptions.ConnectionString);

    private const string _databaseName = nameof(MongoDbStoreOptions.DatabaseName);

    private const string _server = "mongodb://localhost:27017";

    public static TheoryData<string?, string[]> ConnectionStringRows => new()
    {
        { null, [_connectionString] },
        { "", [_connectionString] },
        { "   ", [_connectionString] },
        { _server, [] },
    };

    public static TheoryData<string?, string[]> DatabaseNameRows => new()
    {
        { null, [_databaseName] },
        { "", [_databaseName] },
        { "   ", [_databaseName] },
        { "msghatch", [] },
        { "Msg_Hatch-2", [] },
        { "msg.hatch", [_databaseName] },
        { "msg hatch", [_databaseName] },
        { "msg$hatch", [_databaseName] },
        { "üzenetek", [_databaseName] },
        { new string('a', 62), [] },
        { new string('a', 63), [] },
        { new string('a', 64), [_databaseName] },
    };

    [Fact]
    public void Validate_DefaultOptions_FailsOnlyOnTheConnectionStringAndHasTheDatabaseNameMsghatch()
    {
        // Arrange
        var options = new MongoDbStoreOptions();

        // Act
        var result = new MongoDbStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal([_connectionString], OptionsFailures.FailedProperties(result));
        Assert.Equal("msghatch", options.DatabaseName);
    }

    [Theory]
    [MemberData(nameof(ConnectionStringRows))]
    public void Validate_ConnectionString_FailsWhenItIsMissing(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new MongoDbStoreOptions { ConnectionString = value! };

        // Act
        var result = new MongoDbStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(DatabaseNameRows))]
    public void Validate_DatabaseName_FailsWhenItIsMissingHasAnotherCharacterOrIsLongerThan63Characters(
        string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new MongoDbStoreOptions { ConnectionString = _server, DatabaseName = value! };

        // Act
        var result = new MongoDbStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
