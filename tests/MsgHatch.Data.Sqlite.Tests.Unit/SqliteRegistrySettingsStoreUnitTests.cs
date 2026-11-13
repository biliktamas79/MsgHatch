using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.Sqlite.Tests.Unit;

public sealed class SqliteRegistrySettingsStoreUnitTests()
    : RegistrySettingsStoreTestsBase<SqliteRegistrySettingsStore>(
        new SqliteRegistrySettingsStore(TestDatabases.NewOptions().ConnectionString),
        static store => new SqliteRegistrySettingsStoreTestData(store))
{
    private const string _isoTime = "2030-01-01T00:00:00.0000000Z";

    [Fact]
    public async Task SaveChannelEntryAsync_NewEntry_WritesTheColumnsAsTextAndIntegers()
    {
        // Arrange
        var entry = AChannelEntry(Email, displayName: "Shop email", description: null, enabled: false);

        // Act
        await Store.SaveChannelEntryAsync(entry, Ct);

        // Assert
        Assert.Equal(
            ["Email", "Shop email", null, "0", "1", _isoTime],
            Assert.Single(TestDatabases.Rows(
                Store.ConnectionString,
                "SELECT Channel, DisplayName, Description, CAST(Enabled AS TEXT), CAST(Revision AS TEXT), LastModifiedAtUtc FROM MsgHatchChannelEntry")));
    }

    [Fact]
    public async Task SaveMessageTypeEntryAsync_NewEntry_WritesTheDisabledChannelsAsAJsonArray()
    {
        // Arrange
        var entry = AMessageTypeEntry(TypeA, disabledChannels: [SignalR, Email]);

        // Act
        await Store.SaveMessageTypeEntryAsync(entry, Ct);

        // Assert
        Assert.Equal(
            ["A", "1", """["SignalR","Email"]"""],
            Assert.Single(TestDatabases.Rows(
                Store.ConnectionString, "SELECT Type, CAST(Enabled AS TEXT), DisabledChannels FROM MsgHatchMessageTypeEntry")));
    }

    public static TheoryData<string, string> InvalidRowRows => new()
    {
        { "invalid JSON", "INSERT INTO MsgHatchMessageTypeEntry VALUES ('A', NULL, NULL, 1, '[not json', 1, '2030-01-01T00:00:00.0000000Z')" },
        { "invalid channel name", """INSERT INTO MsgHatchMessageTypeEntry VALUES ('A', NULL, NULL, 1, '["not a name!"]', 1, '2030-01-01T00:00:00.0000000Z')""" },
        { "invalid message type name", "INSERT INTO MsgHatchMessageTypeEntry VALUES ('not a name!', NULL, NULL, 1, '[]', 1, '2030-01-01T00:00:00.0000000Z')" },
        { "invalid time", "INSERT INTO MsgHatchChannelEntry VALUES ('Email', NULL, NULL, 1, 1, 'yesterday')" },
    };

    [Theory]
    [MemberData(nameof(InvalidRowRows))]
    public async Task GetEntriesAsync_RowThatAPersonChangedIntoAnInvalidValue_GivesSerializationFailed(string row, string insert)
    {
        // Arrange
        await Store.GetEntriesAsync(Ct);
        TestDatabases.Execute(Store.ConnectionString, insert);

        // Act
        var entries = await Store.GetEntriesAsync(Ct);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, entries.Error?.Type));
    }

    [Fact]
    public async Task GetEntriesAsync_DatabaseOfAnEarlierPhaseWithNoRegistryTables_CreatesTheTablesAndGivesNoEntries()
    {
        // Arrange
        var connectionString = TestDatabases.NewOptions().ConnectionString;
        TestDatabases.Execute(connectionString, "CREATE TABLE MsgHatchSchema (Version INTEGER); INSERT INTO MsgHatchSchema VALUES (1);");
        var store = new SqliteRegistrySettingsStore(connectionString);

        // Act
        var entries = await store.GetEntriesAsync(Ct);

        // Assert
        TestDatabases.Delete(connectionString);
        Assert.Equal((0, 0), (entries.Value.Channels.Count, entries.Value.MessageTypes.Count));
    }

    [Fact]
    public async Task SaveChannelEntryAsync_DatabaseInAMissingFolder_GivesUnavailable()
    {
        // Arrange
        var store = new SqliteRegistrySettingsStore($"Data Source={Path.Combine(Path.GetTempPath(), "msghatch-tests", Guid.NewGuid().ToString("N"), "missing.db")}");

        // Act
        var error = await store.SaveChannelEntryAsync(AChannelEntry(), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }
}
