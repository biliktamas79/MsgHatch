using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;

namespace ExampleApp;

/// <summary>
/// The router of the message type F (section 4.6). It sends each password reset by email to the one user of the event,
/// to the address and in the language of the event. So this message type needs no subscription.
/// </summary>
public sealed class PasswordResetRouter : IMessageRouter<PasswordReset>
{
    /// <inheritdoc/>
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<PasswordReset> message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var reset = message.Event;
        var route = new MessageRoute(
            new DeliveryTarget(DeliveryTargetKind.User, UserId.From(reset.UserId).Value), EmailChannel.Id, new EmailChannelSettings(),
            reset.Language, reset.Email, TemplateScope: null, Trace: null);
        return ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([route]));
    }
}
