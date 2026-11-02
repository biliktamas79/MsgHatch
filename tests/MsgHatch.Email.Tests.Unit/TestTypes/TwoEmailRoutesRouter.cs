using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>The router of the registration tests: two users on the email channel, in English and in Hungarian, each with a trace.</summary>
public sealed class TwoEmailRoutesRouter<TEvent> : IMessageRouter<TEvent>
{
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TEvent> message, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue(
        [
            TestRoutes.AnEmailRoute("u1", "en", "client1@example.com", trace: new TestTrace("TestRouter")),
            TestRoutes.AnEmailRoute("u2", "hu", "client2@example.com", trace: new TestTrace("TestRouter")),
        ]));
}
