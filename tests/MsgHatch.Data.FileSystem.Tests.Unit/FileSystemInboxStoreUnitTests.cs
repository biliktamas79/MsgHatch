using System.Text;
using MsgHatch.Data.FileSystem.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.FileSystem.Tests.Unit;

public sealed class FileSystemInboxStoreUnitTests()
    : InboxStoreTestsBase<FileSystemInboxStore>(
        new FileSystemInboxStore(StoreFolders.New(), new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        static store => new FileSystemInboxStoreTestData(store))
{
    [Fact]
    public async Task AddAsync_MessageAndTestMessage_StoresTheTestMessageUnderTheTestFolder()
    {
        // Arrange
        var message = AMessage(T1);
        var testMessage = AMessage(T1, flags: MessageFlags.Test);

        // Act
        await TenantStore(T1).AddAsync(message, Ct);
        await TenantStore(T1).AddAsync(testMessage, Ct);

        // Assert
        Assert.Equal(
            [Path.Combine("inbox", "pending"), Path.Combine("test", "inbox", "pending")],
            StoreFolders.MessageFiles(Store.RootPath, "inbox").Select(path => StoreFolders.RelativeFolderOf(Store.RootPath, path)));
    }

    [Fact]
    public async Task ClaimDueAsync_TwentyMessagesWithTheSameDueTime_ClaimsThemInTheOrderOfTheirAdd()
    {
        // Arrange
        var added = await AddInOrderAsync(20);

        // Act
        var result = await ClaimAsync(maxCount: 20);

        // Assert
        Assert.Equal(added, result.Value.Messages.Select(static message => message.Delivery.Id));
    }

    [Fact]
    public async Task ClaimDueAsync_SecondStoreOnTheSameFolder_ClaimsTheMessageOfTheFirstStore()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var second = new FileSystemInboxStore(Store.RootPath, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var result = await second.ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task ClaimDueAsync_FileThatTheCodeCannotRead_MovesItToTheFailedFolderAndClaimsTheOtherMessage()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        WritePendingFile("20300101000000000_0000000000_unreadable.json", "{ not json");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(["20300101000030000_0000000000_unreadable.json"], FileNamesIn("failed"));
    }

    [Fact]
    public async Task ClaimDueAsync_NewerVersionThanTheCode_KeepsTheFileClaimedForANewerInstanceAfterTheLease()
    {
        // Arrange
        WritePendingFile("20300101000000000_0000000000_newer.json", NewerVersionOf(AMessage(T1)));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)Now.AddSeconds(30)), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
        Assert.Equal(["20300101000030000_0000000000_newer.json"], FileNamesIn("claimed"));
    }

    [Fact]
    public async Task AddAsync_MessageOfAnUnregisteredType_ReturnsSerializationFailedAndStoresNothing()
    {
        // Arrange
        var message = AMessage(T1) with { Type = new MessageType("Unknown") };

        // Act
        var error = await TenantStore(T1).AddAsync(message, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, error?.Type);
        Assert.Empty(await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task AddAsync_RootFolderThatIsAFile_ReturnsUnavailable()
    {
        // Arrange
        StoreFolders.BlockWithAFile(Store.RootPath);

        // Act
        var error = await TenantStore(T1).AddAsync(AMessage(T1), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_FileWhoseClaimedNameExistsAlready_SkipsItAndClaimsTheNextFile()
    {
        // Arrange
        var blocked = await AddAsync(AMessage(T1));
        var next = await AddAsync(AMessage(T1));
        WriteFile("claimed", ClaimedNameOf(blocked), "{}");

        // Act
        var result = await ClaimAsync(maxCount: 1);

        // Assert
        Assert.Equal(next.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task ClaimDueAsync_MessageDueHalfAMillisecondLater_ClaimsNothingAndGivesTheNextMillisecondAsNextDue()
    {
        // Arrange
        await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddTicks(5_000)));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)Now.AddMilliseconds(1)), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task AddAsync_IdThatIsAlreadyStored_LeavesNoTemporaryFile()
    {
        // Arrange
        var stored = await AddAsync(AMessage(T1));

        // Act
        await TenantStore(T1).AddAsync(AMessage(T1, id: stored.Delivery.Id), Ct);

        // Assert
        Assert.Empty(Directory.EnumerateFiles(Store.RootPath, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ClaimDueAsync_FileNameWithNoValidTimePrefix_SkipsThatFileAndClaimsTheOtherMessage()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        WritePendingFile("notes.json", "{ not a message }");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(["notes.json"], FileNamesIn("pending"));
    }

    [Fact]
    public async Task ClaimDueAsync_TwentyMessagesFailedInTheOtherOrderWithOneNextTryTime_ClaimsThemInTheOrderOfTheirAdd()
    {
        // Arrange
        var added = await AddInOrderAsync(20);
        await FailInTheOtherOrderAsync((await ClaimAsync(maxCount: 20)).Value.Messages, Now.AddSeconds(60));
        Time.Advance(TimeSpan.FromSeconds(60));

        // Act
        var result = await ClaimAsync(maxCount: 20);

        // Assert
        Assert.Equal(added, result.Value.Messages.Select(static message => message.Delivery.Id));
    }

    private async Task FailInTheOtherOrderAsync(IReadOnlyList<ReceivedMessage> messages, DateTime nextTryAtUtc)
    {
        foreach (var message in messages.Reverse())
        {
            await Store.FailAsync(message, "RateLimited", Now, nextTryAtUtc, Ct);
        }
    }

    private async Task<Guid[]> AddInOrderAsync(int count)
    {
        var messages = Enumerable.Range(0, count).Select(_ => AMessage(T1)).ToArray();
        foreach (var message in messages)
        {
            await AddAsync(message);
        }

        return [.. messages.Select(static message => message.Delivery.Id)];
    }

    private void WritePendingFile(string name, string text) => WriteFile("pending", name, text);

    private void WriteFile(string status, string name, string text)
    {
        var folder = Path.Combine(Store.RootPath, "inbox", status);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    /// <summary>The name that a claim with a lease of 30 seconds gives the file of a message: the lease end replaces the due time.</summary>
    private string ClaimedNameOf(ReceivedMessage message)
    {
        var pending = Path.GetFileName(StoreFolders.MessageFiles(Store.RootPath, "inbox")
            .Single(path => path.Contains(message.Delivery.Id.ToString("D"), StringComparison.Ordinal)));
        return string.Concat("20300101000030000", pending.AsSpan("20300101000000000".Length));
    }

    private string[] FileNamesIn(string status) =>
        [.. Directory.EnumerateFiles(Path.Combine(Store.RootPath, "inbox", status)).Select(static path => Path.GetFileName(path))];

    private static string NewerVersionOf(ReceivedMessage message) =>
        Encoding.UTF8.GetString(new MsgHatchJson(TestContentTypes.ARegistry()).Serialize(message).Value)
            .Replace("\"contentVersion\":2", "\"contentVersion\":3", StringComparison.Ordinal);
}
