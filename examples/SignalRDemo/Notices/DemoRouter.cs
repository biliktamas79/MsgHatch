using MsgHatch.Channels.SignalR;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace SignalRDemo.Notices;

#region router
/// <summary>
/// The router of the host for <see cref="DemoNotice"/> (D315): one SignalR route to the target of the notification. The
/// SignalR transport sends a tenant target to the group of the tenant, a company target to the group of the company, and a
/// user target to the user name of the user.
/// </summary>
public sealed class DemoRouter : IMessageRouter<DemoNotice>
{
    /// <inheritdoc/>
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<DemoNotice> message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var route = new MessageRoute(
            message.Event.Target, SignalRChannel.Id, new SignalRChannelSettings(), "en", Address: null, TemplateScope: null, Trace: null);
        return ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([route]));
    }
}
#endregion
