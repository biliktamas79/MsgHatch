namespace MsgHatch.Domain.Delivery;

/// <summary>The retry summary of one inbox or outbox item (D19). All times are UTC.</summary>
/// <param name="Id">The id of the item.</param>
/// <param name="Status">The status.</param>
/// <param name="TryCount">The number of tries so far.</param>
/// <param name="MaxTryCount">The maximum number of tries.</param>
/// <param name="CreatedAtUtc">The time when the item was made.</param>
/// <param name="LastTryAtUtc">The finish time of the last try, or null.</param>
/// <param name="NextTryAtUtc">The time when the item is due.</param>
/// <param name="LeaseUntilUtc">The end of the lease of a claimed item, or null.</param>
/// <param name="ExpiresAtUtc">The expiry, or null for no expiry.</param>
/// <param name="LastError">The text of the last error, or null.</param>
public readonly record struct DeliveryState(
    Guid Id,
    DeliveryStatus Status,
    int TryCount,
    int MaxTryCount,
    DateTime CreatedAtUtc,
    DateTime? LastTryAtUtc,
    DateTime NextTryAtUtc,
    DateTime? LeaseUntilUtc,
    DateTime? ExpiresAtUtc,
    string? LastError)
{
    /// <summary>
    /// Tells whether the item finished before a time, so that a purge can delete it (D128): its status is
    /// <see cref="DeliveryStatus.Done"/>, <see cref="DeliveryStatus.Failed"/>, or <see cref="DeliveryStatus.Cancelled"/>,
    /// and its finish time is before the time. The finish time is <see cref="LastTryAtUtc"/>, or
    /// <see cref="CreatedAtUtc"/> for an item with no try.
    /// </summary>
    /// <param name="timeUtc">The time.</param>
    /// <returns>True when the item finished before the time.</returns>
    public bool FinishedBefore(DateTime timeUtc) =>
        Status is DeliveryStatus.Done or DeliveryStatus.Failed or DeliveryStatus.Cancelled && FinishedAtUtc() < timeUtc;

    /// <summary>Gives the finish time of a finished item: <see cref="LastTryAtUtc"/>, or <see cref="CreatedAtUtc"/> for an item with no try.</summary>
    /// <returns>The finish time.</returns>
    public DateTime FinishedAtUtc() => LastTryAtUtc ?? CreatedAtUtc;
}
