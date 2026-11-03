using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.SignalR.Tests.Unit.TestTypes;

/// <summary>The router of the registration tests: the user <c>u1</c> and the company <c>c1</c> on the SignalR channel.</summary>
public sealed class SignalRRoutesRouter : IMessageRouter<TestEvent>
{
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TestEvent> message, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([SignalRRoutes.AUserRoute(), SignalRRoutes.ACompanyRoute()]));
}
