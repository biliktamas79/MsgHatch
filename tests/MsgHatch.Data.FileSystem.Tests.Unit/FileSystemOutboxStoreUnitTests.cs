using System.Text;
using System.Text.Json.Nodes;
using MsgHatch.Data.FileSystem.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
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
    private static readonly string _firstStateFile = Path.Combine("states", "0000000001.json");

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
    public async Task ClaimDueAsync_StateFileThatIsNotJson_ReturnsSerializationFailed()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        WriteChannelFile(_firstStateFile, "{ not json");

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
    public async Task ClaimDueAsync_StateFileThatAnotherCallerHoldsOpen_ReturnsUnavailable()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await Store.SetChannelDownAsync(ADownOfAPerson(), Ct);
        using var held = HoldTheFirstStateFileOpen();

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task TryStartChannelCheckAsync_StateFileThatAnotherCallerHoldsOpen_ReturnsFalse()
    {
        // Arrange
        await Store.SetChannelDownAsync(
            new ChannelDown(TestChannel.Id, ChannelDownReason.Errors, Now, Now, 503, "Unavailable", Note: null), Ct);
        using var held = HoldTheFirstStateFileOpen();

        // Act
        var started = await Store.TryStartChannelCheckAsync(TestChannel.Id, Now, Now.AddSeconds(30), Ct);

        // Assert
        Assert.Equal((true, false), (started.IsSuccess, started.Value));
    }

    [Fact]
    public async Task GetAsync_FileThatIsNotJson_ReturnsSerializationFailed()
    {
        // Arrange
        var id = Guid.NewGuid();
        WriteChannelFile($"failed/20300101000000000_0000000000_{id:D}.json", "not json");

        // Act
        var history = await Store.For(TenantKey.None).GetAsync(id, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, history.Error?.Type);
    }

    [Fact]
    public async Task QueryAsync_FileThatIsNotJson_SkipsItAndGivesTheReadableMessages()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage();
        await Store.For(TenantKey.None).AddAsync([message], Ct);
        WriteChannelFile($"failed/20300101000000000_0000000000_{Guid.NewGuid():D}.json", "not json");

        // Act
        var page = await Store.For(TenantKey.None).QueryAsync(new OutboxQuery(), Ct);

        // Assert
        Assert.Equal([message.Delivery.Id], page.Value.Items.Select(static summary => summary.Delivery.Id));
    }

    [Fact]
    public async Task GetChannelStatesAsync_GoldenSampleOfVersion1_GivesTheSampleRow()
    {
        // Arrange
        WriteChannelFile(Path.Combine("states", "0000000003.json"), ReadSample("ChannelState.v1.json"));

        // Act
        var rows = await Store.GetChannelStatesAsync(DateTime.MinValue, Ct);

        // Assert
        Assert.Equal(
            [
                new ChannelState(
                    TestChannel.Id, 3, ChannelStateKind.Down, Now.AddHours(1), ChannelStateKind.Up, TimeSpan.FromMinutes(50), ChannelDownReason.Operator, null,
                    null, null, "maintenance"),
            ],
            rows.Value);
    }

    [Fact]
    public async Task SetChannelDownAsync_ThirdRowOfTheGoldenSample_WritesTheFileOfItsVersionInTheFormatOfTheSample()
    {
        // Arrange
        await Store.SetChannelDownAsync(new ChannelDown(TestChannel.Id, ChannelDownReason.Errors, Now, Now.AddSeconds(30), 503, "Unavailable", Note: null), Ct);
        await Store.SetChannelUpAsync(TestChannel.Id, ChannelDownReason.Errors, Now.AddMinutes(10), Ct);
        Time.Advance(TimeSpan.FromHours(1));

        // Act
        await Store.SetChannelDownAsync(ADownOfAPerson(), Ct);

        // Assert
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(ReadSample("ChannelState.v1.json")),
            JsonNode.Parse(File.ReadAllText(Path.Combine(Store.RootPath, "outbox", "Test", "states", "0000000003.json")))));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    public async Task GetChannelStatesAsync_StateFileThatTheCodeCannotRead_ReturnsSerializationFailed(string text)
    {
        // Arrange
        WriteChannelFile(_firstStateFile, text);

        // Act
        var rows = await Store.GetChannelStatesAsync(DateTime.MinValue, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, rows.Error?.Type);
    }

    [Fact]
    public async Task SetChannelDownAsync_VersionThatAnotherProcessCreatedFirst_KeepsItsFileAndAddsTheNextVersion()
    {
        // Arrange
        var second = new FileSystemOutboxStore(Store.RootPath, new MsgHatchJson(TestContentTypes.ARegistry()));
        await second.SetChannelDownAsync(ADownOfAPerson(), Ct);
        await second.SetChannelUpAsync(TestChannel.Id, reason: null, Now, Ct);

        // Act
        var added = await Store.SetChannelDownAsync(ADownOfAPerson(), Ct);

        // Assert
        Assert.Equal(
            (true, "0000000001.json,0000000002.json,0000000003.json"),
            (added.Value, string.Join(',', Directory.GetFiles(Path.Combine(Store.RootPath, "outbox", "Test", "states")).Select(Path.GetFileName).Order(StringComparer.Ordinal))));
    }

    private static string ReadSample(string fileName)
    {
        using var stream = typeof(FileSystemOutboxStoreUnitTests).Assembly
            .GetManifestResourceStream($"MsgHatch.Data.FileSystem.Tests.Unit.Samples.{fileName}")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private ChannelDown ADownOfAPerson() =>
        new(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance");

    private FileStream HoldTheFirstStateFileOpen() =>
        new(Path.Combine(Store.RootPath, "outbox", "Test", _firstStateFile), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

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
