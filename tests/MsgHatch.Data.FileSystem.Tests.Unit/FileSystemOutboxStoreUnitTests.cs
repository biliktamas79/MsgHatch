using MsgHatch.Data.FileSystem.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.FileSystem.Tests.Unit;

public sealed class FileSystemOutboxStoreUnitTests()
    : OutboxStoreTestsBase<FileSystemOutboxStore>(
        new FileSystemOutboxStore(StoreFolders.New(), new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        static store => new FileSystemOutboxStoreTestData(store))
{
    [Fact]
    public async Task RecordTryAsync_PermanentError_MovesTheFileToTheFailedFolderOfItsChannel()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        await AddAsync(AMessage(T1, channel: TestChannel.SecondId));
        var publishTry = new PublishTry(
            message.Delivery.Id, 1, Now, Now.AddSeconds(1), PublishOutcome.PermanentError, ProviderMessageId: null, 403, "Rejected", ErrorMessage: null);

        // Act
        await Store.RecordTryAsync(message, publishTry, nextTryAtUtc: null, Ct);

        // Assert
        Assert.Equal(
            [Path.Combine("outbox", "Test", "failed"), Path.Combine("outbox", "Second", "pending")],
            StoreFolders.MessageFiles(Store.RootPath, "outbox").Select(path => StoreFolders.RelativeFolderOf(Store.RootPath, path)));
    }

    [Fact]
    public async Task ClaimDueAsync_FileThatTheCodeCannotRead_MovesItToTheFailedFolderOfItsChannelAndClaimsTheOtherMessage()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        WriteChannelFile(Path.Combine("pending", "20300101000000000_0000000000_unreadable.json"), "{ not json");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.True(File.Exists(Path.Combine(Store.RootPath, "outbox", "Test", "failed", "20300101000030000_0000000000_unreadable.json")));
    }

    [Fact]
    public async Task ClaimDueAsync_DownFileThatIsNotJson_ReturnsSerializationFailed()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        WriteChannelFile("down.json", "{ not json");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(Errors.SerializationFailed, result.Error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_SecondStoreOnTheSameFolder_ClaimsTheMessageOfTheFirstStore()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var second = new FileSystemOutboxStore(Store.RootPath, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var result = await second.ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task GetDownChannelsAsync_SecondStoreOnTheSameFolder_ReturnsTheDownStateOfTheFirstStore()
    {
        // Arrange
        var down = new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance");
        await Store.SetChannelDownAsync(down, Ct);
        var second = new FileSystemOutboxStore(Store.RootPath, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var result = await second.GetDownChannelsAsync(Ct);

        // Assert
        Assert.Equal([down], result.Value);
    }

    private void WriteChannelFile(string relativePath, string text)
    {
        var path = Path.Combine(Store.RootPath, "outbox", "Test", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
