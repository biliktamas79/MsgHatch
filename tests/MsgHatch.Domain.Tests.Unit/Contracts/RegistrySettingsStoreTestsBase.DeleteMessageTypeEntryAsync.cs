using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class RegistrySettingsStoreTestsBase<TStore>
{
    [Fact]
    public async Task DeleteMessageTypeEntryAsync_StoredEntry_DeletesOnlyThatEntry()
    {
        // Arrange
        var kept = AMessageTypeEntry(TypeB);
        await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(TypeA), Ct);
        await Store.SaveMessageTypeEntryAsync(kept, Ct);

        // Act
        var error = await Store.DeleteMessageTypeEntryAsync(TypeA, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(FieldsOf(kept), FieldsOf(Assert.Single((await EntriesAsync()).MessageTypes)));
    }

    [Fact]
    public async Task DeleteMessageTypeEntryAsync_UnknownEntry_Succeeds()
    {
        // Arrange
        // Act
        var error = await Store.DeleteMessageTypeEntryAsync(TypeA, Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task DeleteMessageTypeEntryAsync_DeletedEntry_ANewEntryStartsAtTheRevision1()
    {
        // Arrange
        await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(revision: 1), Ct);
        await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(revision: 2), Ct);
        await Store.DeleteMessageTypeEntryAsync(TypeA, Ct);

        // Act
        var error = await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(revision: 1), Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task DeleteMessageTypeEntryAsync_ChannelWithTheSameName_KeepsTheChannelEntry()
    {
        // Arrange
        var kept = AChannelEntry(new ChannelId("A"));
        await Store.SaveChannelEntryAsync(kept, Ct);
        await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(new MessageType("A")), Ct);

        // Act
        await Store.DeleteMessageTypeEntryAsync(new MessageType("A"), Ct);

        // Assert
        var entries = await EntriesAsync();
        Assert.Empty(entries.MessageTypes);
        Assert.Equal(kept, Assert.Single(entries.Channels));
    }
}
