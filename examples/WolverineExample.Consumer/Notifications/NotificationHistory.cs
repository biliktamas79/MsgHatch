using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.SignalR;
using WolverineExample.Contracts;

namespace WolverineExample.Consumer.Notifications;

/// <summary>
/// The catch-up of the SignalR notifications of one user, from the outbox through <see cref="IDeliveryQueries"/>, and the
/// typed envelope of each notification through <see cref="IOutboxStore.GetAsync"/> of the tenant (D290). SignalR
/// delivers a message only to the connections that exist at the send, so a client calls the hub after each connect,
/// with the id of the last notification that it got. The history gives the notifications to the user and to the
/// company of the user that the outbox stored from <see cref="Window"/> before that notification, oldest first. With
/// no last notification, or with an id that is not a SignalR notification of the user or of its company, it gives
/// those of the last <see cref="Window"/>. The client drops the ids that it already has.
/// </summary>
/// <param name="services">The services of the host, with the delivery queries and the outbox store of each tenant.</param>
/// <param name="time">The clock.</param>
public sealed class NotificationHistory(IServiceProvider services, TimeProvider time)
{
    /// <summary>
    /// The time that a catch-up covers before the last notification, or before now. It is the default expiry of a SignalR
    /// message (D94), so a notification that the hub sends after the last one was stored at most this time before it.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    // ponytail: at most 100 notifications for each target, newest first, and one store read for each; page with the cursor of the delivery queries if a catch-up needs more
    private const int _limit = 100;

    /// <summary>Gives the notifications that the user possibly missed, oldest first.</summary>
    /// <param name="tenant">The tenant of the user.</param>
    /// <param name="user">The user.</param>
    /// <param name="company">The company of the user, or null.</param>
    /// <param name="lastMessageId">The id of the last notification that the client got, or null.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The notifications, or the error of the outbox.</returns>
    public async ValueTask<ErrorOr<IReadOnlyList<MissedNotification>>> GetMissedAsync(
        TenantId tenant, UserId user, CompanyId? company, Guid? lastMessageId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredKeyedService<IDeliveryQueries>(TenantKey.For(tenant));
        DeliveryTarget[] targets = company is { } id
            ? [new(DeliveryTargetKind.User, user.Value), new(DeliveryTargetKind.Company, id.Value)]
            : [new(DeliveryTargetKind.User, user.Value)];
        var from = await StartOfAsync(queries, targets, lastMessageId, ct);
        if (from.IsError)
        {
            return from.Error;
        }

        List<OutgoingMessageSummary> found = [];
        foreach (var target in targets)
        {
            var page = await queries.GetOutgoingMessagesAsync(
                new OutboxQuery(Channel: SignalRChannel.Id, Target: target, CreatedFromUtc: from.Value, Limit: _limit), ct);
            if (page.IsError)
            {
                return page.Error;
            }

            found.AddRange(page.Value.Items.Where(summary => summary.Delivery.Status != DeliveryStatus.Cancelled && summary.Delivery.Id != lastMessageId));
        }

        return await ContentOfAsync(scope.ServiceProvider.GetRequiredKeyedService<IOutboxStore>(TenantKey.For(tenant)), found, ct);
    }

    private async ValueTask<ErrorOr<DateTime>> StartOfAsync(
        IDeliveryQueries queries, DeliveryTarget[] targets, Guid? lastMessageId, CancellationToken ct)
    {
        var windowStart = time.GetUtcNow().UtcDateTime - Window;
        if (lastMessageId is not { } id)
        {
            return windowStart;
        }

        var last = await queries.GetOutgoingMessageAsync(id, ct);
        if (last.IsError)
        {
            return last.Error.Type == Errors.NotFound ? windowStart : last.Error;
        }

        var summary = last.Value.Summary;
        return summary.Channel == SignalRChannel.Id && targets.Contains(summary.Target) ? summary.Delivery.CreatedAtUtc - Window : windowStart;
    }

    private static async ValueTask<ErrorOr<IReadOnlyList<MissedNotification>>> ContentOfAsync(
        IOutboxStore outbox, List<OutgoingMessageSummary> found, CancellationToken ct)
    {
        List<MissedNotification> missed = [];
        foreach (var summary in found.OrderBy(static summary => summary.Delivery.CreatedAtUtc).ThenBy(static summary => summary.Delivery.Id))
        {
            var history = await outbox.GetAsync(summary.Delivery.Id, ct);
            if (history.IsError)
            {
                return history.Error;
            }

            if (history.Value?.Message.Content is SignalREnvelope<Notification> envelope)
            {
                missed.Add(new MissedNotification(summary.Type.Name, summary.Delivery.Id, envelope.Payload.Text, summary.Delivery.CreatedAtUtc));
            }
        }

        return missed;
    }
}
