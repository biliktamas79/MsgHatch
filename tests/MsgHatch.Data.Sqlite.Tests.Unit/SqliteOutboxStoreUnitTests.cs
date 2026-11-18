using Microsoft.Data.Sqlite;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.Sqlite.Tests.Unit;

public sealed class SqliteOutboxStoreUnitTests()
    : OutboxStoreTestsBase<SqliteOutboxStore>(
        new SqliteOutboxStore(TestDatabases.NewOptions().ConnectionString, new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        static store => new SqliteOutboxStoreTestData(store))
{
    private const string _isoTime = "2030-01-01T00:00:00.0000000Z";

    private const string _firstWriterChange = "UPDATE MsgHatchOutbox SET LastError = 'first writer'";

    [Fact]
    public async Task AddAsync_MessageWithNoTenantAndATrace_WritesTheTimesAndTheEnumValuesAsTextAndAnEmptyTenant()
    {
        // Arrange
        var message = AMessage(TenantKey.None) with { Trace = new TestTrace("reason") };

        // Act
        await TenantStore(TenantKey.None).AddAsync([message], Ct);

        // Assert
        Assert.Equal(
            ["", "Pending", _isoTime, _isoTime, null, "User", "None", "Trace:Test@1", "[]"],
            Assert.Single(TestDatabases.Rows(
                Store.ConnectionString,
                "SELECT TenantId, Status, CreatedAtUtc, NextTryAtUtc, LeaseUntilUtc, TargetKind, Flags, TraceType, Tries FROM MsgHatchOutbox")));
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
            batch.Select(static (message, index) => new[] { $"{index + 1}", message.Delivery.Id.ToString("D") }),
            TestDatabases.Rows(Store.ConnectionString, "SELECT Sequence, Id FROM MsgHatchOutbox ORDER BY Sequence"));
    }

    [Fact]
    public async Task SetChannelDownAsync_ReasonErrors_WritesTheStateRowWithTheEnumValuesAndTheTimesAsText()
    {
        // Act
        await Store.SetChannelDownAsync(new ChannelDown(TestChannel.Id, ChannelDownReason.Errors, Now, Now, 503, "Unavailable", Note: null), Ct);

        // Assert
        Assert.Equal(
            ["Test", "1", "Down", _isoTime, null, null, "Errors", _isoTime, "503", "Unavailable", null],
            Assert.Single(TestDatabases.Rows(Store.ConnectionString, "SELECT * FROM MsgHatchChannelState")));
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
            [["2", "Up", "Down", "5400000", null, null]],
            TestDatabases.Rows(
                Store.ConnectionString, "SELECT Version, Kind, PreviousKind, PreviousDurationMs, Reason, NextCheckAtUtc FROM MsgHatchChannelState WHERE Version = 2"));
    }

    [Fact]
    public async Task SetChannelDownAsync_DatabaseWithADownRowOfPhase31_AddsTheStateTableAndReadsNoDownState()
    {
        // Arrange
        var connectionString = TestDatabases.NewOptions().ConnectionString;
        TestDatabases.Execute(connectionString, $"""
            CREATE TABLE MsgHatchSchema (Version INTEGER);
            INSERT INTO MsgHatchSchema (Version) VALUES (1);
            CREATE TABLE MsgHatchChannelDown (
                Channel TEXT PRIMARY KEY, Reason TEXT, SinceUtc TEXT, NextCheckAtUtc TEXT, ErrorCode INTEGER, ErrorName TEXT, Note TEXT);
            INSERT INTO MsgHatchChannelDown VALUES ('Test', 'Operator', '{_isoTime}', NULL, NULL, NULL, 'maintenance');
            """);
        var store = new SqliteOutboxStore(connectionString, new MsgHatchJson(TestContentTypes.ARegistry()));

        try
        {
            // Act
            var before = await store.GetDownChannelsAsync(Ct);
            var added = await store.SetChannelDownAsync(
                new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "new"), Ct);

            // Assert
            Assert.Empty(before.Value);
            Assert.True(added.Value);
            Assert.Equal("new", Assert.Single((await store.GetDownChannelsAsync(Ct)).Value).Note);
        }
        finally
        {
            TestDatabases.Delete(connectionString);
        }
    }

    [Fact]
    public async Task ClaimDueAsync_SecondStoreOnTheSameDatabase_ClaimsTheMessageOfTheFirstStore()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var second = new SqliteOutboxStore(Store.ConnectionString, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var result = await second.ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task GetDownChannelsAsync_SecondStoreOnTheSameDatabase_ReturnsTheDownStateOfTheFirstStore()
    {
        // Arrange
        var down = new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance");
        await Store.SetChannelDownAsync(down, Ct);
        var second = new SqliteOutboxStore(Store.ConnectionString, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var result = await second.GetDownChannelsAsync(Ct);

        // Assert
        Assert.Equal([down], result.Value);
    }

    [Fact]
    public async Task ClaimDueAsync_NothingDueWhileAnotherConnectionHoldsAWriteTransaction_CompletesWithNoWriteLock()
    {
        // Arrange
        var later = Now.AddMinutes(1);
        await AddAsync(AMessage(T1, nextTryAtUtc: later));
        using var firstWriter = TestDatabases.HoldWrite(Store.ConnectionString, _firstWriterChange);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)later), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task SetChannelDownAsync_VersionThatAnotherConnectionInsertsAfterTheRead_KeepsThatRowAndAddsNoRow()
    {
        // Arrange
        await Store.GetDownChannelsAsync(Ct);
        using var firstWriter = TestDatabases.HoldWrite(
            Store.ConnectionString,
            $"INSERT INTO MsgHatchChannelState VALUES ('Test', 1, 'Down', '{_isoTime}', NULL, NULL, 'Operator', NULL, NULL, NULL, 'first writer')");
        var change = Task.Run(
            () => Store.SetChannelDownAsync(new ChannelDown(TestChannel.Id, ChannelDownReason.Errors, Now, Now, 503, "Unavailable", Note: null), Ct).AsTask(), Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TimeProvider.System, Ct);

        // Act
        firstWriter.Commit();
        var added = await change;

        // Assert
        Assert.False(added.Value);
        Assert.Equal("first writer", Assert.Single((await Store.GetDownChannelsAsync(Ct)).Value).Note);
    }

    [Fact]
    public async Task AddAsync_WhileAnotherConnectionHoldsAWriteTransaction_WaitsAndCompletesAfterTheCommit()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        var message = AMessage(T1);
        using var firstWriter = TestDatabases.HoldWrite(Store.ConnectionString, _firstWriterChange);
        var add = Task.Run(() => TenantStore(T1).AddAsync([message], Ct).AsTask(), Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TimeProvider.System, Ct);
        var waited = !add.IsCompleted;

        // Act
        firstWriter.Commit();
        var error = await add;

        // Assert
        Assert.Equal((true, (Error?)null), (waited, error));
        Assert.Equal(message.Delivery.Id, (await StoredAsync(message.Delivery.Id)).Delivery.Id);
    }

    [Fact]
    public async Task RecordTryAsync_TryNumberThatIsAlreadyRecorded_ChangesNoRowAndKeepsOneJsonArray()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var publishTry = new PublishTry(
            message.Delivery.Id, 1, Now, Now.AddSeconds(1), PublishOutcome.TransientError, ProviderMessageId: null, 429, "RateLimited", ErrorMessage: null);
        await Store.RecordTryAsync(message, publishTry, Now.AddMinutes(1), Ct);
        var row = RowOf(message);

        // Act
        var error = await Store.RecordTryAsync(message, publishTry, Now.AddMinutes(1), Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(row, RowOf(message));
        Assert.Equal(["array", "1"], Assert.Single(TestDatabases.Rows(Store.ConnectionString, "SELECT json_type(Tries), json_array_length(Tries) FROM MsgHatchOutbox")));
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
        TestDatabases.Execute(Store.ConnectionString, $"UPDATE MsgHatchOutbox SET Content = '{{ not json' WHERE Id = '{unreadable.Delivery.Id:D}'");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(
            ["Failed", "SerializationFailed", null],
            Assert.Single(TestDatabases.Rows(
                Store.ConnectionString,
                $"SELECT Status, substr(LastError, 1, instr(LastError, ':') - 1), LeaseUntilUtc FROM MsgHatchOutbox WHERE Id = '{unreadable.Delivery.Id:D}'")));
    }

    [Fact]
    public async Task ClaimDueAsync_RowWithANewerVersionThanTheCode_StaysPendingWithItsLeaseEndAsTheNextDueTime()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        TestDatabases.Execute(Store.ConnectionString, $"UPDATE MsgHatchOutbox SET ContentVersion = 2 WHERE Id = '{message.Delivery.Id:D}'");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)Now.AddSeconds(30)), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
        Assert.Equal(["Pending", "2030-01-01T00:00:30.0000000Z"], Assert.Single(TestDatabases.Rows(Store.ConnectionString, "SELECT Status, NextTryAtUtc FROM MsgHatchOutbox")));
    }

    [Fact]
    public async Task ClaimDueAsync_DownRowWithAnUnknownReason_ReturnsSerializationFailed()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await Store.SetChannelDownAsync(new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance"), Ct);
        TestDatabases.Execute(Store.ConnectionString, "UPDATE MsgHatchChannelState SET Reason = 'Unknown'");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(Errors.SerializationFailed, result.Error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_DatabaseInAFolderThatDoesNotExist_ReturnsUnavailable()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), "msghatch-tests", Guid.NewGuid().ToString("N"), "msghatch.db");
        var store = new SqliteOutboxStore(
            new SqliteConnectionStringBuilder(Store.ConnectionString) { DataSource = missing }.ToString(), new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var result = await store.ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_FirstOperationOnADatabase_SetsTheWalJournalAndTheSchemaVersion1()
    {
        // Act
        await ClaimAsync();

        // Assert
        Assert.Equal(
            [["wal"], ["1"]],
            [.. TestDatabases.Rows(Store.ConnectionString, "PRAGMA journal_mode"), .. TestDatabases.Rows(Store.ConnectionString, "SELECT Version FROM MsgHatchSchema")]);
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

    private string?[] RowOf(OutgoingMessage message) =>
        Assert.Single(TestDatabases.Rows(Store.ConnectionString, $"SELECT * FROM MsgHatchOutbox WHERE Id = '{message.Delivery.Id:D}'"));
}
