using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class RegistrySettingsStoreTestsBase<TStore>
{
    [Fact]
    public async Task DeleteChannelEntryAsync_StoredEntry_DeletesOnlyThatEntry()
    {
        // Arrange
        var kept = AChannelEntry(SignalR);
        await Store.SaveChannelEntryAsync(AChannelEntry(Email), Ct);
        await Store.SaveChannelEntryAsync(kept, Ct);

        // Act
        var error = await Store.DeleteChannelEntryAsync(Email, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(kept, Assert.Single((await EntriesAsync()).Channels));
    }

    [Fact]
    public async Task DeleteChannelEntryAsync_UnknownEntry_Succeeds()
    {
        // Arrange
        // Act
        var error = await Store.DeleteChannelEntryAsync(Email, Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task DeleteChannelEntryAsync_DeletedEntry_ANewEntryStartsAtTheRevision1()
    {
        // Arrange
        await Store.SaveChannelEntryAsync(AChannelEntry(revision: 1), Ct);
        await Store.SaveChannelEntryAsync(AChannelEntry(revision: 2), Ct);
        await Store.DeleteChannelEntryAsync(Email, Ct);

        // Act
        var error = await Store.SaveChannelEntryAsync(AChannelEntry(revision: 1), Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task DeleteChannelEntryAsync_MessageTypeWithTheSameName_KeepsTheMessageTypeEntry()
    {
        // Arrange
        await Store.SaveChannelEntryAsync(AChannelEntry(new ChannelId("A")), Ct);
        var kept = AMessageTypeEntry(new MessageType("A"));
        await Store.SaveMessageTypeEntryAsync(kept, Ct);

        // Act
        await Store.DeleteChannelEntryAsync(new ChannelId("A"), Ct);

        // Assert
        var entries = await EntriesAsync();
        Assert.Empty(entries.Channels);
        Assert.Equal(FieldsOf(kept), FieldsOf(Assert.Single(entries.MessageTypes)));
    }
}
