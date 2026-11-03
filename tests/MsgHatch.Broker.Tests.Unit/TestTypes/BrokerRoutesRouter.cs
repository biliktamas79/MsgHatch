using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Broker.Tests.Unit.TestTypes;

/// <summary>
/// The router of the registration tests: the company <c>c1</c> with the header <c>x-source</c>, and the user <c>u1</c>
/// with no header, on the broker channel.
/// </summary>
public sealed class BrokerRoutesRouter : IMessageRouter<TestEvent>
{
    public static readonly IReadOnlyDictionary<string, string> Headers = new Dictionary<string, string> { ["x-source"] = "orders" };

    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TestEvent> message, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([BrokerRoutes.ACompanyRoute(headers: Headers), BrokerRoutes.AUserRoute()]));
}
