using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the pipeline tests: one router, composer, and outbox store substitute for each tenant key, which the
/// factory overloads of the setup methods give. Each router gives <see cref="Routes"/>, each composer gives one
/// envelope for each route with the address of the route as its text, and each store keeps what it gets.
/// </summary>
public sealed class PipelineDoubles
{
    private readonly Dictionary<TenantKey, IMessageRouter<TestEvent>> _routers = [];
    private readonly Dictionary<TenantKey, IEnvelopeComposer<TestEvent, TestEnvelope>> _composers = [];
    private readonly Dictionary<TenantKey, IEnvelopeComposer<TestEvent, OtherTestEnvelope>> _secondComposers = [];
    private readonly Dictionary<TenantKey, IOutboxStore> _stores = [];

    public IReadOnlyList<MessageRoute> Routes { get; set; } = [];

    public List<OutgoingMessage> Stored { get; } = [];

    public IMessageRouter<TestEvent> Router(TenantKey key) => GetOrAdd(_routers, key, NewRouter);

    public IEnvelopeComposer<TestEvent, TestEnvelope> Composer(TenantKey key) =>
        GetOrAdd(_composers, key, static () => EchoComposer<TestEvent, TestEnvelope>(static route => new TestEnvelope(TextOf(route))));

    public IEnvelopeComposer<TestEvent, OtherTestEnvelope> SecondComposer(TenantKey key) =>
        GetOrAdd(_secondComposers, key, static () => EchoComposer<TestEvent, OtherTestEnvelope>(static route => new OtherTestEnvelope(TextOf(route))));

    public IOutboxStore Store(TenantKey key) => GetOrAdd(_stores, key, NewStore);

    public ServiceProvider Build(
        TimeProvider time,
        bool enableLogging = true,
        Action<RetryOptions>? channelRetry = null,
        Action<RetryOptions>? withRetry = null,
        Action<MsgHatchBuilder>? more = null)
    {
        var services = TestHost.CreateServices(time);
        var builder = services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddTestChannel()
            .AddSecondTestChannel()
            .UseOutboxStore((_, key) => Store(key))
            .UseOutboxDispatchStore(static _ => Substitute.For<IOutboxDispatchStore>())
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m
                .WithRouter((_, key) => Router(key))
                .AsChannel(
                    TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, (_, key) => Composer(key),
                    channelRetry, channel => channel.WithRetry(o => withRetry?.Invoke(o)))
                .AsChannel(
                    TestChannel.SecondId, "OtherEnvelope", 1, TestJsonContext.Default.OtherTestEnvelope,
                    (_, key) => SecondComposer(key)));
        more?.Invoke(builder);
        return services.Build();
    }

    public static string TextOf(MessageRoute route) => route.Address ?? route.Target.Id;

    private static TValue GetOrAdd<TValue>(Dictionary<TenantKey, TValue> values, TenantKey key, Func<TValue> create)
    {
        if (!values.TryGetValue(key, out var value))
        {
            value = create();
            values.Add(key, value);
        }

        return value;
    }

    public static IMessageRouter<TEvent> RouterGiving<TEvent>(Func<IReadOnlyList<MessageRoute>> routes)
    {
        var router = Substitute.For<IMessageRouter<TEvent>>();
        router.RouteAsync(default!, default)
            .ReturnsForAnyArgs(_ => ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue(routes())));
        return router;
    }

    private IMessageRouter<TestEvent> NewRouter() => RouterGiving<TestEvent>(() => Routes);

    public static IEnvelopeComposer<TEvent, TEnvelope> EchoComposer<TEvent, TEnvelope>(Func<MessageRoute, TEnvelope> envelopeOf)
    {
        var composer = Substitute.For<IEnvelopeComposer<TEvent, TEnvelope>>();
        composer.ComposeAsync(default!, default, default)
            .ReturnsForAnyArgs(call => ValueTask.FromResult(ErrorOr<IReadOnlyList<ErrorOr<TEnvelope>>>.ForValue(
                [.. call.ArgAt<ReadOnlyMemory<MessageRoute>>(1).ToArray().Select(route => ErrorOr<TEnvelope>.ForValue(envelopeOf(route)))])));
        return composer;
    }

    private IOutboxStore NewStore()
    {
        var store = Substitute.For<IOutboxStore>();
        store.AddAsync(default!, default)
            .ReturnsForAnyArgs(call =>
            {
                Stored.AddRange(call.ArgAt<IReadOnlyList<OutgoingMessage>?>(0) ?? []);
                return ValueTask.FromResult<Error?>(null);
            });
        return store;
    }
}
