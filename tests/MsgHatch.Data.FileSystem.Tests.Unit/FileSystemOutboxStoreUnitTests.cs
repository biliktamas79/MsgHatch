using System.Text;
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

    [Fact]
    public async Task AddAsync_MessageAndTestMessage_StoresTheTestMessageUnderTheTestFolder()
    {
        // Arrange
        var message = AMessage(T1);
        var testMessage = AMessage(T1, flags: MessageFlags.Test);

        // Act
        await TenantStore(T1).AddAsync([message, testMessage], Ct);

        // Assert
        Assert.Equal(
            [Path.Combine("outbox", "Test", "pending"), Path.Combine("test", "outbox", "Test", "pending")],
            StoreFolders.MessageFiles(Store.RootPath, "outbox").Select(path => StoreFolders.RelativeFolderOf(Store.RootPath, path)));
    }

    [Fact]
    public async Task ClaimDueAsync_NewerVersionThanTheCode_KeepsTheFileClaimedForANewerInstanceAfterTheLease()
    {
        // Arrange
        WriteChannelFile(Path.Combine("pending", "20300101000000000_0000000000_newer.json"), NewerVersionOf(AMessage(T1)));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)Now.AddSeconds(30)), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
        Assert.True(File.Exists(Path.Combine(Store.RootPath, "outbox", "Test", "claimed", "20300101000030000_0000000000_newer.json")));
    }

    [Fact]
    public async Task ClaimDueAsync_DownFileThatAnotherCallerHoldsOpen_ReturnsUnavailable()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await Store.SetChannelDownAsync(ADownOfAPerson(), Ct);
        using var held = HoldTheDownFileOpen();

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task TryStartChannelCheckAsync_DownFileThatAnotherCallerHoldsOpen_ReturnsFalse()
    {
        // Arrange
        await Store.SetChannelDownAsync(
            new ChannelDown(TestChannel.Id, ChannelDownReason.Errors, Now, Now, 503, "Unavailable", Note: null), Ct);
        using var held = HoldTheDownFileOpen();

        // Act
        var started = await Store.TryStartChannelCheckAsync(TestChannel.Id, Now, Now.AddSeconds(30), Ct);

        // Assert
        Assert.Equal((true, false), (started.IsSuccess, started.Value));
    }

    private ChannelDown ADownOfAPerson() =>
        new(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance");

    private FileStream HoldTheDownFileOpen() =>
        new(Path.Combine(Store.RootPath, "outbox", "Test", "down.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    private static string NewerVersionOf(OutgoingMessage message) =>
        Encoding.UTF8.GetString(new MsgHatchJson(TestContentTypes.ARegistry()).Serialize(message).Value)
            .Replace("\"contentVersion\":1", "\"contentVersion\":2", StringComparison.Ordinal);

    private void WriteChannelFile(string relativePath, string text)
    {
        var path = Path.Combine(Store.RootPath, "outbox", "Test", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
