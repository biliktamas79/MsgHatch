using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IDeliveryQueries"/> (section 4.19, D289, D290) on the substitutes of
/// <see cref="DeliveryTestServices"/>: the channels, the traffic, the pages with each filter and the cursor of the next page,
/// the detail with its trace as JSON text, the content as JSON text, the rules of <see cref="DeliveryRules"/>, and the errors
/// of the stores. Each implementation gets them with one subclass that only builds it.
/// </summary>
/// <typeparam name="T">The implementation.</typeparam>
public abstract class DeliveryQueriesTestsBase<T>
    where T : class, IDeliveryQueries
{
    private static readonly MessageType _type = new("TestEvent");
    private static readonly Error _storeError = new(Errors.Unavailable, "The store is not available.");
    private readonly DeliveryTestServices _services = new();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => _services.Now;

    [Fact]
    public async Task GetChannelsAsync_RegisteredChannels_GivesEachWithItsDownStateItsPendingCountsAndItsOldestDueTime()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var pause = new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "note");
        _services.OutboxDispatch.GetDownChannelsAsync(Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([pause])));
        IReadOnlyList<MessageCount> counts =
        [
            new(_type, TestChannel.Id, DeliveryStatus.Pending, Test: false, 2, Now.AddMinutes(-5)),
            new(_type, TestChannel.Id, DeliveryStatus.Pending, Test: true, 1, Now.AddMinutes(-9)),
            new(new MessageType("OtherTestEvent"), TestChannel.Id, DeliveryStatus.Pending, Test: false, 4, Now.AddMinutes(-1)),
            new(_type, TestChannel.ThirdId, DeliveryStatus.Pending, Test: false, 7, Now),
        ];
        _services.OutboxDispatch.CountAsync(new TrafficQuery(Status: DeliveryStatus.Pending, IncludeTest: true), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue(counts)));

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);

        // Assert
        Assert.Equal(
            [
                (TestChannel.Id, "Test channel", ChannelFeatures.None, (ChannelDown?)pause, 6, 1, (DateTime?)Now.AddMinutes(-9)),
                (TestChannel.SecondId, "Second test channel", ChannelFeatures.GroupDelivery, null, 0, 0, null),
            ],
            channels.Value.Select(static channel => (
                channel.Descriptor.Id, channel.Descriptor.Name, channel.Descriptor.Features, channel.Down, channel.Pending, channel.PendingTest,
                channel.OldestDueAtUtc)));
        Assert.Equal(2, _services.OutboxDispatch.ReceivedCalls().Count());
    }

    [Fact]
    public async Task GetTrafficAsync_EachFilterOfATenant_GivesTheCountsOfTheInboxAndTheOutboxOfTheTenant()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var query = new TrafficQuery(Now.AddHours(-1), Now, DeliveryStatus.Failed, IncludeTest: true);
        IReadOnlyList<MessageCount> inbox = [new(_type, null, DeliveryStatus.Failed, Test: true, 1, null)];
        IReadOnlyList<MessageCount> outbox = [new(_type, TestChannel.Id, DeliveryStatus.Failed, Test: false, 3, null)];
        _services.Inbox.CountAsync(query, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue(inbox)));
        _services.Outbox.CountAsync(query, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue(outbox)));

        // Act
        var report = await host.Service(DeliveryTestServices.T1).GetTrafficAsync(query, Ct);

        // Assert
        Assert.Equal(inbox, report.Value.Inbox);
        Assert.Equal(outbox, report.Value.Outbox);
        Assert.Equal([DeliveryTestServices.T1], _services.OutboxKeys);
    }

    [Fact]
    public async Task GetTrafficAsync_NoInboxStore_GivesEmptyInboxCounts()
    {
        // Arrange
        await using var host = await StartHostAsync(withInbox: false);
        _services.Outbox.CountAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue([])));

        // Act
        var report = await host.Service(TenantKey.None).GetTrafficAsync(new TrafficQuery(), Ct);

        // Assert
        Assert.Empty(report.Value.Inbox);
    }

    [Fact]
    public async Task GetOutgoingMessagesAsync_EachFilterOfATenant_GivesThePageOfTheStoreOfTheTenantWithItsNextCursor()
    {
        // Arrange
        await using var host = await StartHostAsync();
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];
        var query = new OutboxQuery(
            ids, DeliveryStatus.Failed, _type, TestChannel.Id, new DeliveryTarget(DeliveryTargetKind.User, "user 1"), Guid.NewGuid(),
            Now.AddHours(-2), Now, Now.AddMinutes(-5), IncludeTest: true, Limit: 10, new MessageCursor(Now.AddTicks(1234567), Guid.NewGuid()));
        var summary = OutgoingMessageSummary.Of(TestMessages.AnOutgoingMessage(tenant: DeliveryTestServices.T1.Tenant, flags: MessageFlags.Test));
        var next = new MessageCursor(Now.AddTicks(-7), summary.Delivery.Id);
        _services.Outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(DeliveryTestServices.Page(next, summary));

        // Act
        var page = await host.Service(DeliveryTestServices.T1).GetOutgoingMessagesAsync(query, Ct);

        // Assert
        Assert.Equal([summary], page.Value.Items);
        Assert.Equal(next, page.Value.Next);
        Assert.Equal([DeliveryTestServices.T1], _services.OutboxKeys);
        await _services.Outbox.Received(1).QueryAsync(
            Arg.Is<OutboxQuery>(received => DeliveryTestServices.SameQuery(received, query)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOutgoingMessagesAsync_TenantTarget_GivesThePageOfTheStoreWithTheTenantTarget()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var tenantTarget = new DeliveryTarget(DeliveryTargetKind.Tenant, "");
        var query = new OutboxQuery(Target: tenantTarget);
        var summary = OutgoingMessageSummary.Of(TestMessages.AnOutgoingMessage(tenant: DeliveryTestServices.T1.Tenant)) with { Target = tenantTarget };
        _services.Outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(DeliveryTestServices.Page(null, summary));

        // Act
        var page = await host.Service(DeliveryTestServices.T1).GetOutgoingMessagesAsync(query, Ct);

        // Assert
        Assert.Equal([summary], page.Value.Items);
        await _services.Outbox.Received(1).QueryAsync(
            Arg.Is<OutboxQuery>(received => DeliveryTestServices.SameQuery(received, query)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetReceivedMessagesAsync_EachFilter_GivesThePageOfTheStoreWithItsNextCursor()
    {
        // Arrange
        await using var host = await StartHostAsync();
        Guid[] ids = [Guid.NewGuid()];
        var query = new InboxQuery(
            ids, DeliveryStatus.Pending, _type, Now.AddHours(-2), Now, Now.AddMinutes(-5), IncludeTest: false, Limit: 500,
            new MessageCursor(Now.AddTicks(-1), Guid.NewGuid()));
        var summary = DeliveryTestServices.AReceivedSummary();
        var next = new MessageCursor(Now.AddMinutes(-3), summary.Delivery.Id);
        _services.Inbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(DeliveryTestServices.Page(next, summary));

        // Act
        var page = await host.Service(TenantKey.None).GetReceivedMessagesAsync(query, Ct);

        // Assert
        Assert.Equal([summary], page.Value.Items);
        Assert.Equal(next, page.Value.Next);
        await _services.Inbox.Received(1).QueryAsync(
            Arg.Is<InboxQuery>(received => DeliveryTestServices.SameQuery(received, query)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetReceivedMessagesAsync_NoInboxStore_GivesAnEmptyPage()
    {
        // Arrange
        await using var host = await StartHostAsync(withInbox: false);

        // Act
        var page = await host.Service(TenantKey.None).GetReceivedMessagesAsync(new InboxQuery(), Ct);

        // Assert
        Assert.Equal((0, (MessageCursor?)null), (page.Value.Items.Count, page.Value.Next));
    }

    [Fact]
    public async Task GetOutgoingMessageAsync_StoredMessage_GivesItsSummaryItsTraceAsJsonTextAndItsTriesWithNoEnvelope()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var trace = new TestTrace("rule 7");
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope("client1@example.com"), trace: trace);
        IReadOnlyList<PublishTry> tries = [PublishTry.Cancel(message.Delivery.Id, 1, Now)];
        _services.Outbox.GetAsync(message.Delivery.Id, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(new OutgoingMessageHistory(message, tries))));

        // Act
        var detail = await host.Service(TenantKey.None).GetOutgoingMessageAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal(
            (OutgoingMessageSummary.Of(message), JsonSerializer.Serialize(trace, TestJsonContext.Default.TestTrace)),
            (detail.Value.Summary, detail.Value.Trace));
        Assert.Equal(tries, detail.Value.Tries);
    }

    [Fact]
    public async Task GetOutgoingMessageAsync_NoTrace_GivesANullTrace()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var message = TestMessages.AnOutgoingMessage();
        _services.Outbox.GetAsync(default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(new OutgoingMessageHistory(message, []))));

        // Act
        var detail = await host.Service(TenantKey.None).GetOutgoingMessageAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal((message.Delivery.Id, null, 0), (detail.Value.Summary.Delivery.Id, detail.Value.Trace, detail.Value.Tries.Count));
    }

    [Fact]
    public async Task GetOutgoingMessageAsync_TraceOfAnUnregisteredType_GivesSerializationFailed()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var message = TestMessages.AnOutgoingMessage(trace: new UnregisteredTrace("rule"));
        _services.Outbox.GetAsync(default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(new OutgoingMessageHistory(message, []))));

        // Act
        var detail = await host.Service(TenantKey.None).GetOutgoingMessageAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, detail.Error?.Type);
    }

    [Fact]
    public async Task GetOutgoingMessageAsync_UnknownIdOrIdOfAnotherTenant_GivesNotFound()
    {
        // Arrange
        await using var host = await StartHostAsync();
        _services.Outbox.GetAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(null)));

        // Act
        var detail = await host.Service(DeliveryTestServices.T1).GetOutgoingMessageAsync(Guid.NewGuid(), Ct);

        // Assert
        Assert.Equal(Errors.NotFound, detail.Error?.Type);
    }

    [Fact]
    public async Task GetOutgoingContentAsync_StoredMessage_GivesTheWholeMessageWithItsEnvelopeAsTheJsonTextOfMsgHatchJson()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope("client1@example.com"), flags: MessageFlags.Test);
        _services.Outbox.GetAsync(message.Delivery.Id, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(new OutgoingMessageHistory(message, []))));
        var expected = Encoding.UTF8.GetString(host.Server.GetRequiredService<MsgHatchJson>().Serialize(message).Value);

        // Act
        var content = await host.Service(TenantKey.None).GetOutgoingContentAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal(new MessageContent(message.Delivery.Id, expected), content.Value);
    }

    [Fact]
    public async Task GetReceivedContentAsync_StoredMessage_GivesTheWholeMessageWithItsEventAsTheJsonTextOfMsgHatchJson()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var message = TestMessages.AReceivedMessage(new TestEvent("Ősz-űr"));
        _services.Inbox.GetAsync(message.Delivery.Id, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(ErrorOr<ReceivedMessage?>.ForValue(message)));
        var expected = Encoding.UTF8.GetString(host.Server.GetRequiredService<MsgHatchJson>().Serialize(message).Value);

        // Act
        var content = await host.Service(TenantKey.None).GetReceivedContentAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal(new MessageContent(message.Delivery.Id, expected), content.Value);
    }

    [Theory]
    [MemberData(nameof(DeliveryTestServices.UnknownContentRows), MemberType = typeof(DeliveryTestServices))]
    public async Task ContentAsync_UnknownId_GivesNotFound(string row)
    {
        // Arrange
        await using var host = await StartHostAsync(withInbox: row != "received with no inbox store");
        _services.Inbox.GetAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<ReceivedMessage?>.ForValue(null)));
        _services.Outbox.GetAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(null)));
        var queries = host.Service(TenantKey.None);

        // Act
        var content = row == "outgoing"
            ? await queries.GetOutgoingContentAsync(Guid.NewGuid(), Ct)
            : await queries.GetReceivedContentAsync(Guid.NewGuid(), Ct);

        // Assert
        Assert.Equal((row, Errors.NotFound), (row, content.Error?.Type));
    }

    [Theory]
    [MemberData(nameof(DeliveryTestServices.QueryValidationRows), MemberType = typeof(DeliveryTestServices))]
    public async Task Query_ValidationRule_GivesValidationFailedAndCallsNoStoreOnlyWhenInvalid(string row, bool valid)
    {
        // Arrange
        await using var host = await StartHostAsync();

        // Act
        var error = await QueryAsync(host.Service(TenantKey.None), row);

        // Assert
        Assert.Equal((row, !valid, valid), (row, error?.Type == Errors.ValidationFailed, _services.StoreCalls() > 0));
    }

    [Theory]
    [MemberData(nameof(DeliveryTestServices.QueryStoreErrorRows), MemberType = typeof(DeliveryTestServices))]
    public async Task Query_StoreError_GivesTheErrorOfTheStore(string row)
    {
        // Arrange
        await using var host = await StartHostAsync();
        FailTheStoreOf(row);

        // Act
        var error = row == "detail of a message"
            ? (await host.Service(TenantKey.None).GetOutgoingMessageAsync(Guid.NewGuid(), Ct)).Error
            : (await host.Service(TenantKey.None).GetChannelsAsync(Ct)).Error;

        // Assert
        Assert.Equal((row, _storeError), (row, error));
    }

    /// <summary>Starts the host of the implementation on the substitutes of the test.</summary>
    /// <param name="addServerServices">Adds the services of the host that holds the stores.</param>
    /// <returns>The started host.</returns>
    protected abstract ValueTask<IRegistryQueriesTestHost<T>> StartAsync(Action<IServiceCollection> addServerServices);

    private ValueTask<IRegistryQueriesTestHost<T>> StartHostAsync(bool withInbox = true) => StartAsync(services => _services.AddTo(services, withInbox));

    private void FailTheStoreOf(string row)
    {
        _services.OutboxDispatch.GetDownChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([])));
        if (row == "down states of the channels")
        {
            _services.OutboxDispatch.GetDownChannelsAsync(Ct)
                .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForError(_storeError)));
        }
        else if (row == "counts of the channels")
        {
            _services.OutboxDispatch.CountAsync(default!, Ct)
                .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForError(_storeError)));
        }
        else
        {
            _services.Outbox.GetAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForError(_storeError)));
        }
    }

    private async Task<Error?> QueryAsync(IDeliveryQueries queries, string row) => row switch
    {
        "limit 0" => (await queries.GetOutgoingMessagesAsync(new OutboxQuery(Limit: 0), Ct)).Error,
        "limit 1" => (await queries.GetOutgoingMessagesAsync(new OutboxQuery(Limit: 1), Ct)).Error,
        "limit 500" => (await queries.GetOutgoingMessagesAsync(new OutboxQuery(Limit: 500), Ct)).Error,
        "limit 501" => (await queries.GetOutgoingMessagesAsync(new OutboxQuery(Limit: 501), Ct)).Error,
        "ids 0" => (await queries.GetReceivedMessagesAsync(new InboxQuery(Ids: DeliveryTestServices.Ids(0)), Ct)).Error,
        "ids 1" => (await queries.GetReceivedMessagesAsync(new InboxQuery(Ids: DeliveryTestServices.Ids(1)), Ct)).Error,
        "ids 1000" => (await queries.GetReceivedMessagesAsync(new InboxQuery(Ids: DeliveryTestServices.Ids(1000)), Ct)).Error,
        "ids 1001" => (await queries.GetReceivedMessagesAsync(new InboxQuery(Ids: DeliveryTestServices.Ids(1001)), Ct)).Error,
        "created from at created to" => (await queries.GetTrafficAsync(new TrafficQuery(Now, Now), Ct)).Error,
        "created from before created to" => (await queries.GetTrafficAsync(new TrafficQuery(Now.AddTicks(-1), Now), Ct)).Error,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "No query has this row."),
    };

    /// <summary>A route trace that the host does not register.</summary>
    private sealed record UnregisteredTrace(string Reason) : RouteTrace;
}
