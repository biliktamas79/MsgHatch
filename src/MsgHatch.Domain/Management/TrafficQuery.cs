using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Management;

/// <summary>The filters of a count of the traffic (section 4.19). A null filter matches each value.</summary>
/// <param name="CreatedFromUtc">The earliest creation time, included, or null.</param>
/// <param name="CreatedToUtc">The creation time that ends the range, not included, or null.</param>
/// <param name="Status">The status, or null for each status.</param>
/// <param name="IncludeTest">
/// False leaves out the messages with <see cref="MessageFlags.Test"/>. True adds them as rows of their own (D164).
/// </param>
public sealed record TrafficQuery(
    DateTime? CreatedFromUtc = null, DateTime? CreatedToUtc = null, DeliveryStatus? Status = null, bool IncludeTest = false)
{
    /// <summary>Tells whether a message matches the filters, for a store that reads the messages into memory.</summary>
    /// <param name="delivery">The delivery state of the message.</param>
    /// <param name="flags">The flags of the message.</param>
    /// <returns>True when the message matches.</returns>
    public bool Matches(DeliveryState delivery, MessageFlags flags) =>
        QueryFilters.Match(delivery, flags, Status, CreatedFromUtc, CreatedToUtc, dueBeforeUtc: null, IncludeTest);
}
