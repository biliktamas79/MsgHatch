using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class RegistrySettingsStoreTestsBase<TStore>
{
    [Fact]
    public async Task SaveChannelEntryAsync_NewEntryWithRevision1_StoresIt()
    {
        // Arrange
        var entry = AChannelEntry(revision: 1, description: "The email of the shop", enabled: false);

        // Act
        var error = await Store.SaveChannelEntryAsync(entry, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(entry, Assert.Single((await EntriesAsync()).Channels));
    }

    [Fact]
    public async Task SaveChannelEntryAsync_StoredRevisionPlus1_ReplacesTheEntry()
    {
        // Arrange
        await Store.SaveChannelEntryAsync(AChannelEntry(revision: 1), Ct);
        Time.Advance(TimeSpan.FromMinutes(1));
        var update = AChannelEntry(revision: 2, displayName: null, description: "Changed", enabled: false);

        // Act
        var error = await Store.SaveChannelEntryAsync(update, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(update, Assert.Single((await EntriesAsync()).Channels));
    }

    [Theory]
    [MemberData(nameof(RegistrySettingsStoreTestRows.NewEntryRevisionRows), MemberType = typeof(RegistrySettingsStoreTestRows))]
    public async Task SaveChannelEntryAsync_NewEntry_SavesOnlyTheRevision1(int revision, int? expectedCode)
    {
        // Arrange
        var entry = AChannelEntry(revision: revision);

        // Act
        var error = await Store.SaveChannelEntryAsync(entry, Ct);

        // Assert
        Assert.Equal(expectedCode, error?.Type.Code);
    }

    [Theory]
    [MemberData(nameof(RegistrySettingsStoreTestRows.UpdateRevisionRows), MemberType = typeof(RegistrySettingsStoreTestRows))]
    public async Task SaveChannelEntryAsync_StoredRevision1_SavesOnlyTheRevision2(int revision, int? expectedCode)
    {
        // Arrange
        await Store.SaveChannelEntryAsync(AChannelEntry(revision: 1), Ct);

        // Act
        var error = await Store.SaveChannelEntryAsync(AChannelEntry(revision: revision, displayName: "New"), Ct);

        // Assert
        Assert.Equal(expectedCode, error?.Type.Code);
    }

    [Fact]
    public async Task SaveChannelEntryAsync_Conflict_KeepsTheStoredEntry()
    {
        // Arrange
        var stored = AChannelEntry(revision: 1);
        await Store.SaveChannelEntryAsync(stored, Ct);

        // Act
        var error = await Store.SaveChannelEntryAsync(AChannelEntry(revision: 1, displayName: "Other"), Ct);

        // Assert
        Assert.Equal(Errors.Conflict, error?.Type);
        Assert.Equal(stored, Assert.Single((await EntriesAsync()).Channels));
    }

    [Theory]
    [MemberData(nameof(RegistrySettingsStoreTestRows.TextRows), MemberType = typeof(RegistrySettingsStoreTestRows))]
    public async Task SaveChannelEntryAsync_Texts_RoundTripUnchanged(string? displayName, string? description)
    {
        // Arrange
        var entry = AChannelEntry(displayName: displayName, description: description);

        // Act
        await Store.SaveChannelEntryAsync(entry, Ct);

        // Assert
        Assert.Equal(entry, Assert.Single((await EntriesAsync()).Channels));
    }
}
