using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>The test router of the end-to-end test: two users on the test channel, each with a trace.</summary>
public sealed class TwoRouteTestRouter : IMessageRouter<TestEvent>
{
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TestEvent> message, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue(
        [
            TestRoutes.ARoute("u1", address: "client1@example.com", trace: new TestTrace("TestRouter")),
            TestRoutes.ARoute("u2", address: "client2@example.com", trace: new TestTrace("TestRouter")),
        ]));
}
