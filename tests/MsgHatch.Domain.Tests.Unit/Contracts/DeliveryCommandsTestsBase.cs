using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.DeliveryTestServices;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IDeliveryCommands"/> (section 4.19, D289) on the substitutes of <see cref="DeliveryTestServices"/>:
/// the pause and the resume of a channel, the retry and the cancel of the messages of a query page by page, the delete of the
/// test data, the purge, the rules of <see cref="DeliveryRules"/> and of the server, and the errors of the stores. Each
/// implementation gets them with one subclass that only builds it.
/// </summary>
/// <typeparam name="T">The implementation.</typeparam>
public abstract class DeliveryCommandsTestsBase<T>
    where T : class, IDeliveryCommands
{
    private static readonly Error _storeError = new(Errors.Unavailable, "The store is not available.");
    private readonly DeliveryTestServices _services = new();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => _services.Now;

    [Fact]
    public async Task PauseChannelAsync_RegisteredChannel_SetsAnOperatorDownStateWithTheTimeAndTheNote()
    {
        // Arrange
        const string note = "Ősz-űr outage";
        await using var host = await StartHostAsync();
        _services.OutboxDispatch.SetChannelDownAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<bool>.ForValue(true)));

        // Act
        var error = await host.Service(TenantKey.None).PauseChannelAsync(TestChannel.Id, note, Ct);

        // Assert
        Assert.Null(error);
        await _services.OutboxDispatch.Received(1).SetChannelDownAsync(
            new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, note),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeChannelAsync_RegisteredChannel_EndsEachReasonAtTheTimeOfTheClockAndNotifiesTheTopicOfTheChannel()
    {
        // Arrange
        await using var host = await StartHostAsync();
        _services.OutboxDispatch.SetChannelUpAsync(default, default, default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<bool>.ForValue(true)));

        // Act
        var error = await host.Service(TenantKey.None).ResumeChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Null(error);
        await _services.OutboxDispatch.Received(1).SetChannelUpAsync(TestChannel.Id, null, Now, Arg.Any<CancellationToken>());
        await _services.Signal.Received(1).NotifyAsync(DispatchTopic.ForChannel(TestChannel.Id), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task ChannelAction_UnregisteredChannel_GivesNotFoundAndCallsNoStore(string action)
    {
        // Arrange
        await using var host = await StartHostAsync();
        var commands = host.Service(TenantKey.None);
        var unregistered = new ChannelId("Unregistered");

        // Act
        var error = action == "pause"
            ? await commands.PauseChannelAsync(unregistered, note: null, Ct)
            : await commands.ResumeChannelAsync(unregistered, Ct);

        // Assert
        Assert.Equal((Errors.NotFound, 0), (error?.Type, _services.OutboxDispatch.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task RetryOutgoingAsync_ThreePages_CallsTheStoreOneTimeForEachPageAndSumsTheCounts()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var (first, second) = (new MessageCursor(Now, Guid.NewGuid()), new MessageCursor(Now.AddMinutes(-1), Guid.NewGuid()));
        _services.Outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(
            Page(first, ASummary(), ASummary()), Page(second, ASummary(), ASummary()), Page(null, ASummary()));
        _services.Outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(2), Changed(1), Changed(0));

        // Act
        var result = await host.Service(TenantKey.None).RetryOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Equal(new DeliveryActionResult(5, 3), result.Value);
        await _services.Outbox.Received(3).RetryAsync(Arg.Any<IReadOnlyList<Guid>>(), Now, 3, Arg.Any<CancellationToken>());
        await _services.Outbox.Received(1).QueryAsync(
            Arg.Is<OutboxQuery>(query => query.After == second && query.Limit == 500), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryOutgoingAsync_TwoContentTypesOnAPage_GivesEachTheTriesOfItsRetryOptions()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var (withThree, withEight) = (ASummary("Test:TestEnvelope"), ASummary("Test:OtherEnvelope"));
        _services.Outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, withThree, withEight));
        _services.Outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(1));

        // Act
        await host.Service(TenantKey.None).RetryOutgoingAsync(new OutboxQuery(Channel: TestChannel.Id), Ct);

        // Assert
        await _services.Outbox.Received(1).RetryAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { withThree.Delivery.Id })), Now, 3, Arg.Any<CancellationToken>());
        await _services.Outbox.Received(1).RetryAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { withEight.Delivery.Id })), Now, 8, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryOutgoingAsync_Changes_NotifiesTheTopicOfEachChangedChannel()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var onSecond = ASummary("Test:OtherEnvelope") with { Channel = TestChannel.SecondId };
        _services.Outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, ASummary(), onSecond));
        _services.Outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(1), Changed(0));

        // Act
        await host.Service(TenantKey.None).RetryOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Equal(
            [DispatchTopic.ForChannel(TestChannel.Id)],
            _services.Signal.ReceivedCalls().Select(static call => (DispatchTopic)call.GetArguments()[0]!));
    }

    [Fact]
    public async Task RetryOutgoingAsync_NoChange_NotifiesNothing()
    {
        // Arrange
        await using var host = await StartHostAsync();
        _services.Outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, ASummary()));
        _services.Outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(0));

        // Act
        await host.Service(TenantKey.None).RetryOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Empty(_services.Signal.ReceivedCalls());
    }

    [Fact]
    public async Task CancelOutgoingAsync_EachFilterOfATenant_QueriesTheStoreOfTheTenantWithTheFiltersAndNoCursor()
    {
        // Arrange
        await using var host = await StartHostAsync();
        Guid[] ids = [Guid.NewGuid()];
        var query = new OutboxQuery(
            ids, DeliveryStatus.Pending, new MessageType("TestEvent"), TestChannel.Id, new DeliveryTarget(DeliveryTargetKind.Company, "acme 1"),
            Guid.NewGuid(), Now.AddHours(-2), Now, Now.AddMinutes(-5), IncludeTest: true);
        _services.Outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, ASummary()));
        _services.Outbox.CancelAsync(default!, default, Ct).ReturnsForAnyArgs(Changed(1));

        // Act
        var result = await host.Service(T1).CancelOutgoingAsync(query, Ct);
        var expected = query with { Limit = 500 };

        // Assert
        Assert.Equal(new DeliveryActionResult(1, 1), result.Value);
        Assert.Equal([T1], _services.OutboxKeys);
        await _services.Outbox.Received(1).QueryAsync(
            Arg.Is<OutboxQuery>(received => SameQuery(received, expected)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryReceivedAsync_EachFilter_QueriesTheStoreWithTheFiltersAndNoCursor()
    {
        // Arrange
        await using var host = await StartHostAsync();
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];
        var query = new InboxQuery(ids, DeliveryStatus.Failed, new MessageType("TestEvent"), Now.AddHours(-2), Now, Now.AddMinutes(-5), IncludeTest: true);
        _services.Inbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, AReceivedSummary()));
        _services.Inbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(0));

        // Act
        var result = await host.Service(TenantKey.None).RetryReceivedAsync(query, Ct);
        var expected = query with { Limit = 500 };

        // Assert
        Assert.Equal(new DeliveryActionResult(1, 0), result.Value);
        await _services.Inbox.Received(1).QueryAsync(
            Arg.Is<InboxQuery>(received => SameQuery(received, expected)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryReceivedAsync_Changes_RetriesWithTheInboxOptionsAndNotifiesTheInboxTopic()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var summary = AReceivedSummary();
        _services.Inbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, summary));
        _services.Inbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(Changed(1));

        // Act
        var result = await host.Service(TenantKey.None).RetryReceivedAsync(new InboxQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Equal(new DeliveryActionResult(1, 1), result.Value);
        await _services.Inbox.Received(1).RetryAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { summary.Delivery.Id })), Now, 8, Arg.Any<CancellationToken>());
        await _services.Signal.Received(1).NotifyAsync(DispatchTopic.Inbox, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelReceivedAsync_Page_CancelsTheIdsOfThePage()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var summary = AReceivedSummary();
        _services.Inbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, summary));
        _services.Inbox.CancelAsync(default!, default, Ct).ReturnsForAnyArgs(Changed(1));

        // Act
        var result = await host.Service(TenantKey.None).CancelReceivedAsync(new InboxQuery(Status: DeliveryStatus.Pending), Ct);

        // Assert
        Assert.Equal(new DeliveryActionResult(1, 1), result.Value);
        await _services.Inbox.Received(1).CancelAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { summary.Delivery.Id })), Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelOutgoingAsync_TwoPages_SumsTheCountsAndNotifiesNothing()
    {
        // Arrange
        await using var host = await StartHostAsync();
        _services.Outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(new MessageCursor(Now, Guid.NewGuid()), ASummary()), Page(null, ASummary()));
        _services.Outbox.CancelAsync(default!, default, Ct).ReturnsForAnyArgs(Changed(1), Changed(0));

        // Act
        var result = await host.Service(TenantKey.None).CancelOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Pending), Ct);

        // Assert
        Assert.Equal((new DeliveryActionResult(2, 1), 0), (result.Value, _services.Signal.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task RetryReceivedAsync_NoInboxStore_ChangesNothing()
    {
        // Arrange
        await using var host = await StartHostAsync(withInbox: false);

        // Act
        var result = await host.Service(TenantKey.None).RetryReceivedAsync(new InboxQuery(Status: DeliveryStatus.Failed), Ct);

        // Assert
        Assert.Equal(new DeliveryActionResult(0, 0), result.Value);
    }

    [Fact]
    public async Task PurgeAsync_MaxCount100_CallsEachDispatchStoreWithMaxCount100AndGivesBothCounts()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var before = Now.AddDays(-30);
        _services.InboxDispatch.PurgeAsync(before, 100, Arg.Any<CancellationToken>()).Returns(Changed(7));
        _services.OutboxDispatch.PurgeAsync(before, 100, Arg.Any<CancellationToken>()).Returns(Changed(9));

        // Act
        var deleted = await host.Service(TenantKey.None).PurgeAsync(before, 100, Ct);

        // Assert
        Assert.Equal(new DeletedMessages(7, 9), deleted.Value);
    }

    [Fact]
    public async Task DeleteTestDataAsync_TwoDispatchStores_CallsBothAndGivesBothCounts()
    {
        // Arrange
        await using var host = await StartHostAsync();
        _services.InboxDispatch.DeleteTestDataAsync(Ct).ReturnsForAnyArgs(Changed(2));
        _services.OutboxDispatch.DeleteTestDataAsync(Ct).ReturnsForAnyArgs(Changed(5));

        // Act
        var deleted = await host.Service(TenantKey.None).DeleteTestDataAsync(Ct);

        // Assert
        Assert.Equal(new DeletedMessages(2, 5), deleted.Value);
    }

    [Theory]
    [MemberData(nameof(CommandValidationRows), MemberType = typeof(DeliveryTestServices))]
    public async Task Command_ValidationRule_GivesValidationFailedAndCallsNoStoreOnlyWhenInvalid(string row, bool valid)
    {
        // Arrange
        await using var host = await StartHostAsync();

        // Act
        var error = await CommandAsync(host, row);

        // Assert
        Assert.Equal((row, !valid, valid), (row, error?.Type == Errors.ValidationFailed, _services.StoreCalls() > 0));
    }

    [Theory]
    [MemberData(nameof(CommandStoreErrorRows), MemberType = typeof(DeliveryTestServices))]
    public async Task Command_StoreError_GivesTheErrorOfTheStore(string row)
    {
        // Arrange
        await using var host = await StartHostAsync();
        var commands = host.Service(TenantKey.None);
        var failed = new OutboxQuery(Status: DeliveryStatus.Failed);
        _services.Outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, ASummary()));
        _services.InboxDispatch.PurgeAsync(default, default, Ct).ReturnsForAnyArgs(Changed(0));

        // Act
        var error = row switch
        {
            "pause" => await FailAsync(
                () => _services.OutboxDispatch.SetChannelDownAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<bool>.ForError(_storeError))),
                async () => await commands.PauseChannelAsync(TestChannel.Id, note: null, Ct)),
            "resume" => await FailAsync(
                () => _services.OutboxDispatch.SetChannelUpAsync(default, default, default, Ct)
                    .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<bool>.ForError(_storeError))),
                async () => await commands.ResumeChannelAsync(TestChannel.Id, Ct)),
            "test data of the inbox" => await FailAsync(
                () => _services.InboxDispatch.DeleteTestDataAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForError(_storeError))),
                async () => (await commands.DeleteTestDataAsync(Ct)).Error),
            "purge of the outbox" => await FailAsync(
                () => _services.OutboxDispatch.PurgeAsync(default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForError(_storeError))),
                async () => (await commands.PurgeAsync(Now, 10, Ct)).Error),
            "query of a retry" => await FailAsync(
                () => _services.Outbox.QueryAsync(default!, Ct)
                    .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForError(_storeError))),
                async () => (await commands.RetryOutgoingAsync(failed, Ct)).Error),
            "retry of a page" => await FailAsync(
                () => _services.Outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForError(_storeError))),
                async () => (await commands.RetryOutgoingAsync(failed, Ct)).Error),
            _ => await FailAsync(
                () => _services.Outbox.CancelAsync(default!, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForError(_storeError))),
                async () => (await commands.CancelOutgoingAsync(failed, Ct)).Error),
        };

        // Assert
        Assert.Equal((row, _storeError), (row, error));
    }

    /// <summary>Starts the host of the implementation on the substitutes of the test.</summary>
    /// <param name="addServerServices">Adds the services of the host that holds the stores.</param>
    /// <returns>The started host.</returns>
    protected abstract ValueTask<IRegistryQueriesTestHost<T>> StartAsync(Action<IServiceCollection> addServerServices);

    private ValueTask<IRegistryQueriesTestHost<T>> StartHostAsync(bool withInbox = true) => StartAsync(services => _services.AddTo(services, withInbox));

    private static async Task<Error?> FailAsync(Action configure, Func<Task<Error?>> call)
    {
        configure();
        return await call();
    }

    private async Task<Error?> CommandAsync(IRegistryQueriesTestHost<T> host, string row)
    {
        var commands = host.Service(TenantKey.None);
        var tenant = host.Service(T1);
        return row switch
        {
            "note 500" => await commands.PauseChannelAsync(TestChannel.Id, new string('n', 500), Ct),
            "note 501" => await commands.PauseChannelAsync(TestChannel.Id, new string('n', 501), Ct),
            "outbox retry with only limit and include test" =>
                (await commands.RetryOutgoingAsync(new OutboxQuery(IncludeTest: true, Limit: 10), Ct)).Error,
            "outbox cancel with only limit and include test" =>
                (await commands.CancelOutgoingAsync(new OutboxQuery(IncludeTest: true, Limit: 10), Ct)).Error,
            "inbox retry with only limit and include test" =>
                (await commands.RetryReceivedAsync(new InboxQuery(IncludeTest: true, Limit: 10), Ct)).Error,
            "inbox cancel with only limit and include test" =>
                (await commands.CancelReceivedAsync(new InboxQuery(IncludeTest: true, Limit: 10), Ct)).Error,
            "outbox retry with limit 0" => (await commands.RetryOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Failed, Limit: 0), Ct)).Error,
            "purge max count 0" => (await commands.PurgeAsync(Now, 0, Ct)).Error,
            "purge max count 1" => (await commands.PurgeAsync(Now, 1, Ct)).Error,
            "purge before now" => (await commands.PurgeAsync(Now, 10, Ct)).Error,
            "purge after now" => (await commands.PurgeAsync(Now.AddTicks(1), 10, Ct)).Error,
            "pause of a tenant" => await tenant.PauseChannelAsync(TestChannel.Id, note: null, Ct),
            "resume of a tenant" => await tenant.ResumeChannelAsync(TestChannel.Id, Ct),
            "delete test data of a tenant" => (await tenant.DeleteTestDataAsync(Ct)).Error,
            "purge of a tenant" => (await tenant.PurgeAsync(Now, 10, Ct)).Error,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "No command has this row."),
        };
    }
}
