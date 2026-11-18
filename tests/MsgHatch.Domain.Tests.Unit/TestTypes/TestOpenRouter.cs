using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An open generic router for <c>UseRouter</c>. It gives no route.</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
public sealed class TestOpenRouter<TEvent> : IMessageRouter<TEvent>
{
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TEvent> message, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([]));
}

/// <summary>The factories of the open routers of the tests, for <c>UseRouter</c>.</summary>
public static class TestOpenRouter
{
    /// <summary>Makes a <see cref="TestOpenRouter{TEvent}"/> for each event type.</summary>
    public static IOpenRouterFactory Factory { get; } = new EmptyRouterFactory();

    /// <summary>Makes no router: each call throws, as a factory does for an event type that it cannot route.</summary>
    public static IOpenRouterFactory Failing { get; } = new FailingRouterFactory();

    private sealed class EmptyRouterFactory : IOpenRouterFactory
    {
        public IMessageRouter<TEvent> Create<TEvent>(IServiceProvider tenantServices, TenantKey key) => new TestOpenRouter<TEvent>();
    }

    private sealed class FailingRouterFactory : IOpenRouterFactory
    {
        public IMessageRouter<TEvent> Create<TEvent>(IServiceProvider tenantServices, TenantKey key) =>
            throw new InvalidOperationException($"The test factory makes no router for {typeof(TEvent).Name}.");
    }
}
