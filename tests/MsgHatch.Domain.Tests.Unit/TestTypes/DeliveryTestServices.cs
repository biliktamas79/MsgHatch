using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the tests of the delivery queries and commands (section 4.19): substitutes for the stores and the dispatch
/// signal, the fake clock, and the setup of a host on them. The host has the channels Test and Second, the message types
/// <c>TestEvent</c> and <c>OtherTestEvent</c>, and the trace type <c>Trace:Test</c>. The test channel registers the content
/// type <c>Test:TestEnvelope</c> with three tries and <c>Test:OtherEnvelope</c> with the default of eight tries.
/// </summary>
public sealed class DeliveryTestServices
{
    public static readonly TenantKey T1 = TenantKey.For(new TenantId("t1"));

    public static TheoryData<string> UnknownContentRows => ["received", "outgoing", "received with no inbox store"];

    /// <summary>Each rule of a query, with a valid row and an invalid row at each limit: a valid row calls a store, an invalid row calls none.</summary>
    public static TheoryData<string, bool> QueryValidationRows => new()
    {
        { "limit 0", false },
        { "limit 1", true },
        { "limit 500", true },
        { "limit 501", false },
        { "ids 0", false },
        { "ids 1", true },
        { "ids 1000", true },
        { "ids 1001", false },
        { "created from at created to", false },
        { "created from before created to", true },
    };

    public static TheoryData<string> QueryStoreErrorRows => ["down states of the channels", "counts of the channels", "detail of a message"];

    /// <summary>
    /// Each rule of a command, with a valid row and an invalid row at each limit: a valid row calls a store, an invalid row calls
    /// none. The rule of the finish time of a purge needs the clock of the server.
    /// </summary>
    public static TheoryData<string, bool> CommandValidationRows => new()
    {
        { "note 500", true },
        { "note 501", false },
        { "outbox retry with only limit and include test", false },
        { "outbox cancel with only limit and include test", false },
        { "inbox retry with only limit and include test", false },
        { "inbox cancel with only limit and include test", false },
        { "outbox retry with limit 0", false },
        { "purge max count 0", false },
        { "purge max count 1", true },
        { "purge before now", true },
        { "purge after now", false },
        { "pause of a tenant", false },
        { "resume of a tenant", false },
        { "delete test data of a tenant", false },
        { "purge of a tenant", false },
    };

    public static TheoryData<string> CommandStoreErrorRows =>
    [
        "pause",
        "resume",
        "test data of the inbox",
        "purge of the outbox",
        "query of a retry",
        "retry of a page",
        "cancel of a page",
    ];

    public FakeTimeProvider Time { get; } = TestHost.NewFakeTime();

    public IOutboxStore Outbox { get; } = Substitute.For<IOutboxStore>();

    public IInboxStore Inbox { get; } = Substitute.For<IInboxStore>();

    public IOutboxDispatchStore OutboxDispatch { get; } = Substitute.For<IOutboxDispatchStore>();

    public IInboxDispatchStore InboxDispatch { get; } = Substitute.For<IInboxDispatchStore>();

    public IDispatchSignal Signal { get; } = Substitute.For<IDispatchSignal>();

    /// <summary>The tenant key of each resolution of the outbox store, so that a test sees the tenant of a call.</summary>
    public List<TenantKey> OutboxKeys { get; } = [];

    public DateTime Now => Time.GetUtcNow().UtcDateTime;

    /// <summary>Adds the fake clock and MsgHatch with the substitutes. With no inbox, the host has no inbox stores.</summary>
    public MsgHatchBuilder AddTo(IServiceCollection services, bool withInbox = true)
    {
        services.AddSingleton<TimeProvider>(Time);
        var builder = services.AddMsgHatch()
            .AddTestChannel()
            .AddSecondTestChannel()
            .UseRouter(TestOpenRouter.Factory)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .AsChannel(
                    TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>(),
                    configure: static channel => channel.WithRetry(static o => o.MaxTryCount = 3)))
            .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                .AsChannel(TestChannel.Id, "OtherEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<OtherTestEvent>()))
            .AddTraceType("Test", 1, TestJsonContext.Default.TestTrace)
            .UseOutboxStore((_, key) =>
            {
                OutboxKeys.Add(key);
                return Outbox;
            })
            .UseOutboxDispatchStore(_ => OutboxDispatch)
            .UseDispatchSignal(_ => Signal);
        return withInbox ? builder.UseInboxStore((_, _) => Inbox).UseInboxDispatchStore(_ => InboxDispatch) : builder;
    }

    /// <summary>The number of calls of the four stores.</summary>
    public int StoreCalls() =>
        Outbox.ReceivedCalls().Count() + Inbox.ReceivedCalls().Count() + OutboxDispatch.ReceivedCalls().Count()
        + InboxDispatch.ReceivedCalls().Count();

    public static OutgoingMessageSummary ASummary(string contentType = "Test:TestEnvelope") =>
        OutgoingMessageSummary.Of(TestMessages.AnOutgoingMessage()) with { ContentType = contentType };

    public static ReceivedMessageSummary AReceivedSummary() => ReceivedMessageSummary.Of(TestMessages.AReceivedMessage(new TestEvent("text")));

    public static ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> Page(MessageCursor? next, params OutgoingMessageSummary[] items) =>
        ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(new MessagePage<OutgoingMessageSummary>(items, next)));

    public static ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> Page(MessageCursor? next, params ReceivedMessageSummary[] items) =>
        ValueTask.FromResult(ErrorOr<MessagePage<ReceivedMessageSummary>>.ForValue(new MessagePage<ReceivedMessageSummary>(items, next)));

    public static ValueTask<ErrorOr<int>> Changed(int count) => ValueTask.FromResult(ErrorOr<int>.ForValue(count));

    public static Guid[] Ids(int count) => [.. Enumerable.Range(0, count).Select(static _ => Guid.NewGuid())];

    /// <summary>True when a query equals the expected query, with the ids compared item by item.</summary>
    public static bool SameQuery(OutboxQuery query, OutboxQuery expected) =>
        query with { Ids = null } == expected with { Ids = null } && SameIds(query.Ids, expected.Ids);

    /// <summary>True when a query equals the expected query, with the ids compared item by item.</summary>
    public static bool SameQuery(InboxQuery query, InboxQuery expected) =>
        query with { Ids = null } == expected with { Ids = null } && SameIds(query.Ids, expected.Ids);

    private static bool SameIds(IReadOnlyList<Guid>? ids, IReadOnlyList<Guid>? expected) =>
        ids is null ? expected is null : expected is not null && ids.SequenceEqual(expected);
}
