using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Management;

/// <summary>The filters that the inbox query, the outbox query, and the traffic query share (section 4.19).</summary>
internal static class QueryFilters
{
    public static bool Match(
        DeliveryState delivery, MessageFlags flags, DeliveryStatus? status, DateTime? createdFromUtc, DateTime? createdToUtc,
        DateTime? dueBeforeUtc, bool includeTest) =>
        (status is null || status == delivery.Status)
        && (createdFromUtc is null || delivery.CreatedAtUtc >= createdFromUtc)
        && (createdToUtc is null || delivery.CreatedAtUtc < createdToUtc)
        && (dueBeforeUtc is null || (delivery.Status == DeliveryStatus.Pending && delivery.NextTryAtUtc < dueBeforeUtc))
        && (includeTest || !flags.HasFlag(MessageFlags.Test));
}
