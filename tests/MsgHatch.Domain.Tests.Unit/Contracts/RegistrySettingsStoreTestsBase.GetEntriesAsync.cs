namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class RegistrySettingsStoreTestsBase<TStore>
{
    [Fact]
    public async Task GetEntriesAsync_EmptyStorage_GivesNoEntries()
    {
        // Arrange
        // Act
        var entries = await Store.GetEntriesAsync(Ct);

        // Assert
        Assert.Equal((0, 0), (entries.Value.Channels.Count, entries.Value.MessageTypes.Count));
    }

    [Fact]
    public async Task GetEntriesAsync_EntriesSavedOutOfOrder_GivesEachListInTheOrdinalOrderOfTheNames()
    {
        // Arrange
        await Store.SaveChannelEntryAsync(AChannelEntry(SignalR), Ct);
        await Store.SaveChannelEntryAsync(AChannelEntry(Broker), Ct);
        await Store.SaveChannelEntryAsync(AChannelEntry(Email), Ct);
        await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(TypeB), Ct);
        await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(TypeA), Ct);

        // Act
        var entries = await EntriesAsync();

        // Assert
        Assert.Equal([Broker, Email, SignalR], entries.Channels.Select(static entry => entry.Channel));
        Assert.Equal([TypeA, TypeB], entries.MessageTypes.Select(static entry => entry.Type));
    }
}
