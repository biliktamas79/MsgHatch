using MsgHatch.Data.EntityFrameworkCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EntityFrameworkCore.Tests.Integration;

public sealed class EntityFrameworkRegistrySettingsStoreIntegTests(SqlServerFixture server)
    : RegistrySettingsStoreTestsBase<EntityFrameworkRegistrySettingsStore>(
        new EntityFrameworkRegistrySettingsStore(server.Database("registry").Contexts),
        _ => new EntityFrameworkRegistrySettingsStoreTestData(server.Database("registry")))
{
    private const string _isoTime = "2030-01-01T00:00:00.0000000Z";

    public static TheoryData<string, string> InvalidRowRows => new()
    {
        { "invalid JSON", "INSERT INTO [msghatch].[MsgHatchMessageTypeEntry] VALUES ('A', NULL, NULL, 1, '[not json', 1, '2030-01-01')" },
        { "invalid channel name", """INSERT INTO [msghatch].[MsgHatchMessageTypeEntry] VALUES ('A', NULL, NULL, 1, '["not a name!"]', 1, '2030-01-01')""" },
        { "invalid message type name", "INSERT INTO [msghatch].[MsgHatchMessageTypeEntry] VALUES ('not a name!', NULL, NULL, 1, '[]', 1, '2030-01-01')" },
        { "invalid channel entry name", "INSERT INTO [msghatch].[MsgHatchChannelEntry] VALUES ('not a name!', NULL, NULL, 1, 1, '2030-01-01')" },
    };

    private TestDatabase Database => server.Database("registry");

    [Fact]
    public async Task SaveChannelEntryAsync_NewEntry_WritesTheColumns()
    {
        // Arrange
        var entry = AChannelEntry(Email, displayName: "Shop email", description: null, enabled: false);

        // Act
        await Store.SaveChannelEntryAsync(entry, Ct);

        // Assert
        Assert.Equal(
            ["Email", "Shop email", null, "0", "1", _isoTime],
            Assert.Single(await Database.RowsAsync(
                "SELECT Channel, DisplayName, Description, Enabled, Revision, LastModifiedAtUtc FROM [msghatch].[MsgHatchChannelEntry]", Ct)));
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
            Assert.Single(await Database.RowsAsync("SELECT Type, Enabled, DisabledChannels FROM [msghatch].[MsgHatchMessageTypeEntry]", Ct)));
    }

    [Theory]
    [MemberData(nameof(InvalidRowRows))]
    public async Task GetEntriesAsync_RowThatAPersonChangedIntoAnInvalidValue_GivesSerializationFailed(string row, string insert)
    {
        // Arrange
        await Database.ExecuteAsync(insert, Ct);

        // Act
        var entries = await Store.GetEntriesAsync(Ct);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, entries.Error?.Type));
    }

    [Fact]
    public async Task SaveChannelEntryAsync_DatabaseThatDoesNotExist_GivesUnavailable()
    {
        // Arrange
        var store = new EntityFrameworkRegistrySettingsStore(server.Database("missing").Contexts);

        // Act
        var error = await store.SaveChannelEntryAsync(AChannelEntry(), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }
}
