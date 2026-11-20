using System.Globalization;
using System.Text;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AdminUI;

/// <summary>The addresses of the delivery pages of the core admin UI, so that a host or another page can link to a page (D295).</summary>
public static class DeliveryAdminRoutes
{
    /// <summary>The address of the outbox list.</summary>
    public const string Outbox = "/msghatch/outbox";

    /// <summary>The address of the inbox list.</summary>
    public const string Inbox = "/msghatch/inbox";

    internal const string OutgoingMessageTemplate = Outbox + "/{Id:guid}";

    internal const string ReceivedMessageTemplate = Inbox + "/{Id:guid}";

    /// <summary>Gives the address of the page of one outgoing message (D296).</summary>
    /// <param name="id">The id of the outgoing message.</param>
    /// <returns>For example <c>/msghatch/outbox/7d2e…</c>.</returns>
    public static string OutgoingMessage(Guid id) => $"{Outbox}/{id.ToString("D", CultureInfo.InvariantCulture)}";

    /// <summary>Gives the address of the page of one received message (D296).</summary>
    /// <param name="id">The id of the received message.</param>
    /// <returns>For example <c>/msghatch/inbox/7d2e…</c>.</returns>
    public static string ReceivedMessage(Guid id) => $"{Inbox}/{id.ToString("D", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Gives the address of the outbox list with filters (D295). A time is UTC, and goes into the address to the second.
    /// </summary>
    /// <param name="status">The status, or null for each status.</param>
    /// <param name="type">The message type, or null.</param>
    /// <param name="channel">The channel, or null.</param>
    /// <param name="target">The delivery target, or null.</param>
    /// <param name="sourceMessageId">The id of the event that gave the messages, or null.</param>
    /// <param name="createdFromUtc">The earliest creation time, included, or null.</param>
    /// <param name="createdToUtc">The creation time that ends the range, not included, or null.</param>
    /// <param name="includeTest">True also shows the test messages.</param>
    /// <returns>For example <c>/msghatch/outbox?status=Failed&amp;channel=Email</c>, with each value escaped.</returns>
    public static string OutboxOf(
        DeliveryStatus? status = null, MessageType? type = null, ChannelId? channel = null, DeliveryTarget? target = null,
        Guid? sourceMessageId = null, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, bool includeTest = false) =>
        Outbox + QueryOf(
            ("status", status?.ToString()), ("type", type?.Name), ("channel", channel?.Name), ("targetKind", target?.Kind.ToString()),
            ("targetId", target?.Id), ("source", sourceMessageId?.ToString("D", CultureInfo.InvariantCulture)),
            ("from", TimeOf(createdFromUtc)), ("to", TimeOf(createdToUtc)), ("test", includeTest ? "true" : null));

    /// <summary>Gives the address of the inbox list with filters (D295). A time is UTC, and goes into the address to the second.</summary>
    /// <param name="status">The status, or null for each status.</param>
    /// <param name="type">The message type, or null.</param>
    /// <param name="createdFromUtc">The earliest creation time, included, or null.</param>
    /// <param name="createdToUtc">The creation time that ends the range, not included, or null.</param>
    /// <param name="includeTest">True also shows the test messages.</param>
    /// <returns>For example <c>/msghatch/inbox?status=Failed</c>, with each value escaped.</returns>
    public static string InboxOf(
        DeliveryStatus? status = null, MessageType? type = null, DateTime? createdFromUtc = null, DateTime? createdToUtc = null, bool includeTest = false) =>
        Inbox + QueryOf(
            ("status", status?.ToString()), ("type", type?.Name), ("from", TimeOf(createdFromUtc)), ("to", TimeOf(createdToUtc)),
            ("test", includeTest ? "true" : null));

    private static string? TimeOf(DateTime? utc) => utc is { } time ? DeliveryTexts.InputTimeOf(time) : null;

    private static string QueryOf(params (string Name, string? Value)[] values)
    {
        var query = new StringBuilder();
        foreach (var (name, value) in values)
        {
            if (value is not null)
            {
                query.Append(query.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
            }
        }

        return query.ToString();
    }
}
