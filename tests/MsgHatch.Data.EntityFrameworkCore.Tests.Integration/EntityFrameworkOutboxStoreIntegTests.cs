using MsgHatch.Data.EntityFrameworkCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EntityFrameworkCore.Tests.Integration;

public sealed class EntityFrameworkOutboxStoreIntegTests(SqlServerFixture server)
    : OutboxStoreTestsBase<EntityFrameworkOutboxStore>(
        new EntityFrameworkOutboxStore(server.Database("outbox").Contexts, new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new EntityFrameworkOutboxStoreTestData(server.Database("outbox")))
{
    private const string _isoTime = "2030-01-01T00:00:00.0000000Z";

    private TestDatabase Database => server.Database("outbox");

    [Fact]
    public async Task AddAsync_MessageWithNoTenantAndATrace_WritesTheEnumValuesAsTextAnEmptyTenantAndTheTraceType()
    {
        // Arrange
        var message = AMessage(TenantKey.None) with { Trace = new TestTrace("reason") };

        // Act
        await TenantStore(TenantKey.None).AddAsync([message], Ct);

        // Assert
        Assert.Equal(
            ["", "Pending", _isoTime, _isoTime, null, "User", "None", "Trace:Test@1", "[]"],
            Assert.Single(await Database.RowsAsync(
                "SELECT TenantId, Status, CreatedAtUtc, NextTryAtUtc, LeaseUntilUtc, TargetKind, Flags, TraceType, Tries FROM [msghatch].[MsgHatchOutbox]", Ct)));
    }

    [Fact]
    public async Task AddAsync_BatchOfThreeMessages_NumbersTheRowsInTheOrderOfTheBatch()
    {
        // Arrange
        OutgoingMessage[] batch = [AMessage(T1), AMessage(T1), AMessage(T1)];

        // Act
        await TenantStore(T1).AddAsync(batch, Ct);

        // Assert
        Assert.Equal(
            batch.Select(static message => new[] { message.Delivery.Id.ToString("D") }),
            await Database.RowsAsync("SELECT Id FROM [msghatch].[MsgHatchOutbox] ORDER BY Sequence", Ct));
    }

    [Fact]
    public async Task AddAsync_MessageOfAnUnregisteredType_ReturnsSerializationFailedAndAddsNothingOfTheBatch()
    {
        // Arrange
        OutgoingMessage[] batch = [AMessage(T1), AMessage(T1) with { ContentType = "Test:Unknown" }];

        // Act
        var error = await TenantStore(T1).AddAsync(batch, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, error?.Type);
        Assert.Empty(await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task SetChannelDownAsync_ReasonErrors_WritesTheReasonAndTheTimes()
    {
        // Act
        await Store.SetChannelDownAsync(new ChannelDown(TestChannel.Id, ChannelDownReason.Errors, Now, Now, 503, "Unavailable", Note: null), Ct);

        // Assert
        Assert.Equal(
            ["Test", "Errors", _isoTime, _isoTime, "503", "Unavailable", null],
            Assert.Single(await Database.RowsAsync(
                "SELECT Channel, Reason, SinceUtc, NextCheckAtUtc, ErrorCode, ErrorName, Note FROM [msghatch].[MsgHatchChannelDown]", Ct)));
    }

    [Fact]
    public async Task SetChannelDownAsync_ReasonErrorsTwoTimes_ReturnsFalseWithNoErrorTheSecondTime()
    {
        // Arrange
        var down = new ChannelDown(TestChannel.Id, ChannelDownReason.Errors, Now, Now, 503, "Unavailable", Note: null);
        await Store.SetChannelDownAsync(down, Ct);

        // Act
        var again = await Store.SetChannelDownAsync(down with { ErrorCode = 429 }, Ct);

        // Assert
        Assert.Equal(((Error?)null, false), (again.Error, again.Value));
    }

    [Fact]
    public async Task PurgeAsync_MaxCountOne_DeletesTheMessageThatFinishedFirst()
    {
        // Arrange
        var later = await AddAsync(Finished(AMessage(T1), DeliveryStatus.Done, Now.AddDays(-1)));
        await AddAsync(Finished(AMessage(T1), DeliveryStatus.Done, Now.AddDays(-2)));

        // Act
        await Store.PurgeAsync(Now, 1, Ct);

        // Assert
        Assert.Equal([later.Delivery.Id], (await TestData.GetStoredAsync(Ct)).Select(static message => message.Delivery.Id));
    }

    [Fact]
    public async Task ClaimDueAsync_SecondStoreOnTheSameDatabase_ClaimsTheMessageOfTheFirstStore()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var second = new EntityFrameworkOutboxStore(Database.Contexts, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var result = await second.ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task RecordTryAsync_TryNumberThatIsAlreadyRecorded_ChangesNoRowAndKeepsOneJsonArray()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var publishTry = new PublishTry(
            message.Delivery.Id, 1, Now, Now.AddSeconds(1), PublishOutcome.TransientError, ProviderMessageId: null, 429, "RateLimited", ErrorMessage: null);
        await Store.RecordTryAsync(message, publishTry, Now.AddMinutes(1), Ct);
        var row = await RowOfAsync(message);

        // Act
        var error = await Store.RecordTryAsync(message, publishTry, Now.AddMinutes(1), Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(row, await RowOfAsync(message));
        Assert.Equal(
            ["1", "1"],
            Assert.Single(await Database.RowsAsync("SELECT ISJSON(Tries), (SELECT COUNT(*) FROM OPENJSON(Tries)) FROM [msghatch].[MsgHatchOutbox]", Ct)));
    }

    [Fact]
    public async Task ClaimDueAsync_ClaimOfMaxCountRows_GivesNowAsTheNextDueTimeBecauseMoreRowsCanBeDue()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddSeconds(5)));

        // Act
        var result = await ClaimAsync(maxCount: 1);

        // Assert
        Assert.Equal((1, (DateTime?)Now), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task ClaimDueAsync_TwentyMessagesWithTheSameDueTime_ClaimsThemInTheOrderOfTheirAdd()
    {
        // Arrange
        OutgoingMessage[] messages = [.. Enumerable.Range(0, 20).Select(_ => AMessage(T1))];
        await TenantStore(T1).AddAsync(messages, Ct);

        // Act
        var result = await ClaimAsync(maxCount: 20);

        // Assert
        Assert.Equal(messages.Select(static m => m.Delivery.Id), result.Value.Messages.Select(static m => m.Delivery.Id));
    }

    [Fact]
    public async Task ClaimDueAsync_RowWithAnEnvelopeThatIsNotJson_FailsTheRowAndClaimsTheOtherMessage()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        var message = await AddAsync(AMessage(T1));
        await Database.ExecuteAsync($"UPDATE [msghatch].[MsgHatchOutbox] SET Content = '{{ not json' WHERE Id = '{unreadable.Delivery.Id:D}'", Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(
            ["Failed", "SerializationFailed", null],
            Assert.Single(await Database.RowsAsync(
                $"SELECT Status, LEFT(LastError, CHARINDEX(':', LastError) - 1), LeaseUntilUtc FROM [msghatch].[MsgHatchOutbox] WHERE Id = '{unreadable.Delivery.Id:D}'",
                Ct)));
    }

    [Fact]
    public async Task ClaimDueAsync_RowWithANewerVersionThanTheCode_StaysPendingWithItsLeaseEndAsTheNextDueTime()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        await Database.ExecuteAsync($"UPDATE [msghatch].[MsgHatchOutbox] SET ContentVersion = 2 WHERE Id = '{message.Delivery.Id:D}'", Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)Now.AddSeconds(30)), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
        Assert.Equal(
            ["Pending", "2030-01-01T00:00:30.0000000Z"],
            Assert.Single(await Database.RowsAsync("SELECT Status, NextTryAtUtc FROM [msghatch].[MsgHatchOutbox]", Ct)));
    }

    [Fact]
    public async Task ClaimDueAsync_DownRowWithAnUnknownReason_ReturnsSerializationFailed()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await Store.SetChannelDownAsync(
            new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance"), Ct);
        await Database.ExecuteAsync("UPDATE [msghatch].[MsgHatchChannelDown] SET Reason = 'Unknown'", Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(Errors.SerializationFailed, result.Error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_DatabaseThatDoesNotExist_ReturnsUnavailable()
    {
        // Arrange
        var store = new EntityFrameworkOutboxStore(server.Database("missing").Contexts, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var result = await store.ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task QueryAsync_ClaimedMessage_GivesEachTimeWithTheKindUtc()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await ClaimAsync();

        // Act
        var page = await TenantStore(T1).QueryAsync(new OutboxQuery(), Ct);

        // Assert
        var delivery = Assert.Single(page.Value.Items).Delivery;
        Assert.Equal([DateTimeKind.Utc, DateTimeKind.Utc, DateTimeKind.Utc], [delivery.CreatedAtUtc.Kind, delivery.NextTryAtUtc.Kind, delivery.LeaseUntilUtc!.Value.Kind]);
    }

    private async Task<string?[]> RowOfAsync(OutgoingMessage message) =>
        Assert.Single(await Database.RowsAsync($"SELECT * FROM [msghatch].[MsgHatchOutbox] WHERE Id = '{message.Delivery.Id:D}'", Ct));
}
