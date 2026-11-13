using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class RegistrySettingsStoreTestsBase<TStore>
{
    [Theory]
    [MemberData(nameof(RegistrySettingsStoreTestRows.DisabledChannelRows), MemberType = typeof(RegistrySettingsStoreTestRows))]
    public async Task SaveMessageTypeEntryAsync_DisabledChannels_RoundTripUnchanged(string row, ChannelId[] disabledChannels)
    {
        // Arrange
        var entry = AMessageTypeEntry(disabledChannels: disabledChannels, description: "The order is shipped", enabled: false);

        // Act
        var error = await Store.SaveMessageTypeEntryAsync(entry, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal((row, FieldsOf(entry)), (row, FieldsOf(Assert.Single((await EntriesAsync()).MessageTypes))));
    }

    [Fact]
    public async Task SaveMessageTypeEntryAsync_StoredRevisionPlus1_ReplacesTheEntry()
    {
        // Arrange
        await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(revision: 1, disabledChannels: [SignalR]), Ct);
        Time.Advance(TimeSpan.FromMinutes(1));
        var update = AMessageTypeEntry(revision: 2, disabledChannels: [Email], displayName: null, description: "Changed");

        // Act
        var error = await Store.SaveMessageTypeEntryAsync(update, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(FieldsOf(update), FieldsOf(Assert.Single((await EntriesAsync()).MessageTypes)));
    }

    [Theory]
    [MemberData(nameof(RegistrySettingsStoreTestRows.NewEntryRevisionRows), MemberType = typeof(RegistrySettingsStoreTestRows))]
    public async Task SaveMessageTypeEntryAsync_NewEntry_SavesOnlyTheRevision1(int revision, int? expectedCode)
    {
        // Arrange
        var entry = AMessageTypeEntry(revision: revision);

        // Act
        var error = await Store.SaveMessageTypeEntryAsync(entry, Ct);

        // Assert
        Assert.Equal(expectedCode, error?.Type.Code);
    }

    [Theory]
    [MemberData(nameof(RegistrySettingsStoreTestRows.UpdateRevisionRows), MemberType = typeof(RegistrySettingsStoreTestRows))]
    public async Task SaveMessageTypeEntryAsync_StoredRevision1_SavesOnlyTheRevision2(int revision, int? expectedCode)
    {
        // Arrange
        await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(revision: 1), Ct);

        // Act
        var error = await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(revision: revision, displayName: "New"), Ct);

        // Assert
        Assert.Equal(expectedCode, error?.Type.Code);
    }

    [Fact]
    public async Task SaveMessageTypeEntryAsync_Conflict_KeepsTheStoredEntry()
    {
        // Arrange
        var stored = AMessageTypeEntry(revision: 1, disabledChannels: [SignalR]);
        await Store.SaveMessageTypeEntryAsync(stored, Ct);

        // Act
        var error = await Store.SaveMessageTypeEntryAsync(AMessageTypeEntry(revision: 3, disabledChannels: [Email]), Ct);

        // Assert
        Assert.Equal(Errors.Conflict, error?.Type);
        Assert.Equal(FieldsOf(stored), FieldsOf(Assert.Single((await EntriesAsync()).MessageTypes)));
    }

    [Theory]
    [MemberData(nameof(RegistrySettingsStoreTestRows.TextRows), MemberType = typeof(RegistrySettingsStoreTestRows))]
    public async Task SaveMessageTypeEntryAsync_Texts_RoundTripUnchanged(string? displayName, string? description)
    {
        // Arrange
        var entry = AMessageTypeEntry(displayName: displayName, description: description);

        // Act
        await Store.SaveMessageTypeEntryAsync(entry, Ct);

        // Assert
        Assert.Equal(FieldsOf(entry), FieldsOf(Assert.Single((await EntriesAsync()).MessageTypes)));
    }
}
