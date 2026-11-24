using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Unit;

/// <summary>The outbox contract on the EF Core SQLite provider, which has no claim statement, so the store claims with LINQ (D318).</summary>
public sealed class EntityFrameworkOutboxStoreUnitTests : OutboxStoreTestsBase<EntityFrameworkOutboxStore>
{
    private readonly SqliteTestDatabase _database;

    public EntityFrameworkOutboxStoreUnitTests()
        : this(new SqliteTestDatabase())
    {
    }

    private EntityFrameworkOutboxStoreUnitTests(SqliteTestDatabase database)
        : base(
            new EntityFrameworkOutboxStore(database.Contexts, new MsgHatchJson(TestContentTypes.ARegistry())),
            static (store, key) => store.For(key),
            _ => new EntityFrameworkOutboxStoreTestData(database))
    {
        _database = database;
    }

    [Fact]
    public async Task AddAsync_MessageWithNoTenantAndATrace_WritesTheEnumValuesAsTextAnEmptyTenantTheTraceTypeAndNoTries()
    {
        // Arrange
        var message = AMessage(TenantKey.None) with { Trace = new TestTrace("reason") };

        // Act
        await TenantStore(TenantKey.None).AddAsync([message], Ct);

        // Assert
        Assert.Equal(
            ["", "Pending", "2030-01-01 00:00:00", null, "User", "None", "Trace:Test@1", "[]", null, null],
            Assert.Single(_database.Rows(
                "SELECT TenantId, Status, NextTryAtUtc, LeaseUntilUtc, TargetKind, Flags, TraceType, Tries, ClaimToken, ClaimedDueAtUtc FROM MsgHatchOutbox")));
    }

    [Fact]
    public async Task AddAsync_BatchWithAStoredIdInTheMiddle_AddsTheOtherMessagesInTheOrderOfTheBatch()
    {
        // Arrange
        var stored = await AddAsync(AMessage(T1));
        OutgoingMessage[] batch = [AMessage(T1), stored, AMessage(T1)];

        // Act
        var error = await TenantStore(T1).AddAsync(batch, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(
            [[stored.Delivery.Id.ToString("D")], [batch[0].Delivery.Id.ToString("D")], [batch[2].Delivery.Id.ToString("D")]],
            _database.Rows("SELECT Id FROM MsgHatchOutbox ORDER BY Sequence"));
    }

    [Fact]
    public async Task ClaimDueAsync_DueMessage_WritesTheClaimTokenAndTheDueTimeBeforeTheClaim()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddSeconds(-5)));

        // Act
        await ClaimAsync();

        // Assert
        var row = Assert.Single(_database.Rows($"SELECT ClaimToken, ClaimedDueAtUtc, NextTryAtUtc, LeaseUntilUtc FROM MsgHatchOutbox WHERE Id = '{message.Delivery.Id:D}'"));
        Assert.Equal(
            (36, "2029-12-31 23:59:55", "2030-01-01 00:00:30", "2030-01-01 00:00:30"),
            (row[0]!.Length, row[1], row[2], row[3]));
    }

    [Fact]
    public async Task ClaimDueAsync_FirstCandidatesThatAnotherDispatcherTookFirst_ClaimsTheNextDueMessagesOfTheChannel()
    {
        // Arrange
        var interceptor = new FirstClaimUpdateInterceptor(_database, "UPDATE MsgHatchOutbox SET NextTryAtUtc = '2030-01-01 00:00:30' WHERE Sequence <= 2");
        var store = new EntityFrameworkOutboxStore(
            _database.ContextsOf<MsgHatchDbContext>(o => o.AddInterceptors(interceptor)), new MsgHatchJson(TestContentTypes.ARegistry()));
        OutgoingMessage[] messages = [AMessage(T1), AMessage(T1), AMessage(T1), AMessage(T1)];
        await TenantStore(T1).AddAsync(messages, Ct);

        // Act
        var result = await store.ClaimDueAsync(TestChannel.Id, 2, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(
            [messages[2].Delivery.Id, messages[3].Delivery.Id],
            result.Value.Messages.Select(static message => message.Delivery.Id));
    }

    [Fact]
    public async Task RecordTryAsync_TriesThatAPersonChangedIntoInvalidJson_ReturnsSerializationFailed()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        _database.Execute($"UPDATE MsgHatchOutbox SET Tries = '[not json' WHERE Id = '{message.Delivery.Id:D}'");
        var publishTry = new PublishTry(message.Delivery.Id, 1, Now, Now, PublishOutcome.Succeeded, "provider-1", null, null, null);

        // Act
        var error = await Store.RecordTryAsync(message, publishTry, nextTryAtUtc: null, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_RowWithAnEnvelopeThatIsNotJson_FailsTheRowAndClaimsTheOtherMessage()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        var message = await AddAsync(AMessage(T1));
        _database.Execute($"UPDATE MsgHatchOutbox SET Content = '{{ not json' WHERE Id = '{unreadable.Delivery.Id:D}'");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(
            ["Failed", null],
            Assert.Single(_database.Rows($"SELECT Status, LeaseUntilUtc FROM MsgHatchOutbox WHERE Id = '{unreadable.Delivery.Id:D}'")));
    }

    [Fact]
    public async Task ClaimDueAsync_RowWithANewerVersionThanTheCode_StaysPendingWithItsLeaseEndAsTheNextDueTime()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        _database.Execute($"UPDATE MsgHatchOutbox SET ContentVersion = 2 WHERE Id = '{message.Delivery.Id:D}'");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)Now.AddSeconds(30)), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
        Assert.Equal(["Pending", "2030-01-01 00:00:30"], Assert.Single(_database.Rows("SELECT Status, NextTryAtUtc FROM MsgHatchOutbox")));
    }

    [Fact]
    public async Task ClaimDueAsync_DownRowWithAnUnknownReason_ReturnsSerializationFailed()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await Store.SetChannelDownAsync(
            new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance"), Ct);
        _database.Execute("UPDATE MsgHatchChannelState SET Reason = 'Unknown'");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(Errors.SerializationFailed, result.Error?.Type);
    }

    [Fact]
    public async Task SetChannelUpAsync_DownChannel_WritesTheDurationOfTheDownStateAsWholeMilliseconds()
    {
        // Arrange
        await Store.SetChannelDownAsync(new ChannelDown(TestChannel.Id, ChannelDownReason.Errors, Now, Now, 503, "Unavailable", Note: null), Ct);

        // Act
        await Store.SetChannelUpAsync(TestChannel.Id, reason: null, Now.AddMinutes(90), Ct);

        // Assert
        Assert.Equal(
            ["2", "Up", "Down", "5400000", null, null],
            Assert.Single(_database.Rows("SELECT Version, Kind, PreviousKind, PreviousDurationMs, Reason, NextCheckAtUtc FROM MsgHatchChannelState WHERE Version = 2")));
    }

    [Fact]
    public async Task ClaimDueAsync_TableThatDoesNotExist_ReturnsUnavailable()
    {
        // Arrange
        _database.Execute("DROP TABLE MsgHatchOutbox");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }
}
