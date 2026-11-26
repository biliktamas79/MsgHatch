using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.Tests.Integration;

public sealed class EntityFrameworkRegistrySettingsStoreIntegTests(SqlServerFixture server)
    : RegistrySettingsStoreTestsBase<EntityFrameworkRegistrySettingsStore>(
        new EntityFrameworkRegistrySettingsStore(server.Database("registry").Contexts),
        _ => new EntityFrameworkRegistrySettingsStoreTestData(server.Database("registry")))
{
    private const string _isoTime = "2030-01-01T00:00:00.0000000Z";

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
                "SELECT Channel, DisplayName, Description, Enabled, Revision, LastModifiedAtUtc FROM [msghatch].[ChannelEntry]", Ct)));
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
            Assert.Single(await Database.RowsAsync("SELECT Type, Enabled, DisabledChannels FROM [msghatch].[MessageTypeEntry]", Ct)));
    }
}
