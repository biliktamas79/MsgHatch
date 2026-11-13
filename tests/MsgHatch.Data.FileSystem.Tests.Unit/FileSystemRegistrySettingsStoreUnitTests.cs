using MsgHatch.Data.FileSystem.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.FileSystem.Tests.Unit;

public sealed class FileSystemRegistrySettingsStoreUnitTests()
    : RegistrySettingsStoreTestsBase<FileSystemRegistrySettingsStore>(
        new FileSystemRegistrySettingsStore(StoreFolders.New()),
        static store => new FileSystemRegistrySettingsStoreTestData(store))
{
    [Fact]
    public async Task SaveChannelEntryAsync_NewEntry_WritesTheFileOfTheChannelWithCamelCaseNames()
    {
        // Arrange
        var entry = AChannelEntry(Email, displayName: "Shop email", description: null, enabled: false);

        // Act
        await Store.SaveChannelEntryAsync(entry, Ct);

        // Assert
        Assert.Equal(
            """{"channel":"Email","displayName":"Shop email","description":null,"enabled":false,"revision":1,"lastModifiedAtUtc":"2030-01-01T00:00:00Z"}""",
            await File.ReadAllTextAsync(Path.Combine(Store.RootPath, "registry", "channels", "Email.json"), Ct));
    }

    [Fact]
    public async Task SaveMessageTypeEntryAsync_NewEntry_WritesTheFileOfTheMessageTypeWithTheChannelNames()
    {
        // Arrange
        var entry = AMessageTypeEntry(TypeA, disabledChannels: [SignalR, Email], displayName: null);

        // Act
        await Store.SaveMessageTypeEntryAsync(entry, Ct);

        // Assert
        Assert.Equal(
            """{"type":"A","displayName":null,"description":null,"enabled":true,"disabledChannels":["SignalR","Email"],"revision":1,"lastModifiedAtUtc":"2030-01-01T00:00:00Z"}""",
            await File.ReadAllTextAsync(Path.Combine(Store.RootPath, "registry", "message-types", "A.json"), Ct));
    }

    public static TheoryData<string, string> UnreadableFileRows => new()
    {
        { "channels", "{ not json" },
        { "channels", "null" },
        { "message-types", """{"type":"not a name!","enabled":true,"disabledChannels":[],"revision":1,"lastModifiedAtUtc":"2030-01-01T00:00:00Z"}""" },
        { "message-types", "null" },
        { "message-types", """{"type":"A","enabled":true,"disabledChannels":["not a name!"],"revision":1,"lastModifiedAtUtc":"2030-01-01T00:00:00Z"}""" },
    };

    [Theory]
    [MemberData(nameof(UnreadableFileRows))]
    public async Task GetEntriesAsync_FileThatTheCodeCannotRead_GivesSerializationFailed(string folder, string content)
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(Store.RootPath, "registry", folder));
        await File.WriteAllTextAsync(Path.Combine(Store.RootPath, "registry", folder, "X.json"), content, Ct);

        // Act
        var entries = await Store.GetEntriesAsync(Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, entries.Error?.Type);
    }

    [Fact]
    public async Task SaveChannelEntryAsync_UpdateOfAFileWithTheJsonNull_GivesSerializationFailed()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(Store.RootPath, "registry", "channels"));
        await File.WriteAllTextAsync(Path.Combine(Store.RootPath, "registry", "channels", "Email.json"), "null", Ct);

        // Act
        var error = await Store.SaveChannelEntryAsync(AChannelEntry(Email, revision: 2), Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, error?.Type);
    }

    [Fact]
    public async Task SaveChannelEntryAsync_FileThatAnotherWriterHolds_GivesUnavailable()
    {
        // Arrange
        await Store.SaveChannelEntryAsync(AChannelEntry(Email, revision: 1), Ct);
        await using var held = new FileStream(
            Path.Combine(Store.RootPath, "registry", "channels", "Email.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Act
        var error = await Store.SaveChannelEntryAsync(AChannelEntry(Email, revision: 2), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    [Fact]
    public async Task SaveChannelEntryAsync_RegistryFolderThatIsAFile_GivesUnavailable()
    {
        // Arrange
        StoreFolders.BlockWithAFile(Path.Combine(Store.RootPath, "registry"));

        // Act
        var error = await Store.SaveChannelEntryAsync(AChannelEntry(Email, revision: 1), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }
}
