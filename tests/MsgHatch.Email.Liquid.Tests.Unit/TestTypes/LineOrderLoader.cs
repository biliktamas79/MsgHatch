using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Email.Liquid.Tests.Unit.TestTypes;

/// <summary>
/// The payload loader of the Liquid registrations: one order with two lines for each route, with the target id as its
/// name. Only the order of the user <c>u1</c> has a note.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
public sealed class LineOrderLoader<TEvent> : IPayloadLoader<TEvent, LineOrderEmail>
{
    public ValueTask<ErrorOr<IReadOnlyList<LineOrderEmail>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<LineOrderEmail>>.ForValue(
            [.. routes.ToArray().Select(static route => LineOrderEmail.AnOrder(route.Target.Id, route.Target.Id == "u1" ? "Leave it at the door" : null))]));
}
