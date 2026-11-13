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

    /// <summary>Tells whether a claim leased the item, and the lease did not end at <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>True for an active lease.</returns>
    public bool HasActiveLease(DateTime nowUtc) => LeaseUntilUtc > nowUtc;

    /// <summary>
    /// Gives the state after a retry (D111), or null when the retry rules skip the item: a failed item gets the status
    /// <see cref="DeliveryStatus.Pending"/>, the next try time <paramref name="nowUtc"/>, and <see cref="TryCount"/>
    /// plus <paramref name="addedTries"/> as its <see cref="MaxTryCount"/>. A pending item with no active lease is due
    /// at <paramref name="nowUtc"/>. A done or cancelled item, an item with an active lease, and an item whose expiry is
    /// at or before <paramref name="nowUtc"/> do not change.
    /// </summary>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="addedTries">The number of new tries of a failed item.</param>
    /// <returns>The new state, or null for no change.</returns>
    public DeliveryState? RetriedAt(DateTime nowUtc, int addedTries) =>
        ExpiresAtUtc <= nowUtc || HasActiveLease(nowUtc) ? null
        : Status switch
        {
            DeliveryStatus.Failed => this with
            {
                Status = DeliveryStatus.Pending, NextTryAtUtc = nowUtc, MaxTryCount = TryCount + addedTries, LeaseUntilUtc = null,
            },
            DeliveryStatus.Pending => this with { NextTryAtUtc = nowUtc, LeaseUntilUtc = null },
            _ => null,
        };

    /// <summary>
    /// Gives the state after a cancel (D110), or null when the cancel rules skip the item: only a pending item with no
    /// active lease gets the status <see cref="DeliveryStatus.Cancelled"/>. It keeps its tries and its last error.
    /// </summary>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>The new state, or null for no change.</returns>
    public DeliveryState? CancelledAt(DateTime nowUtc) =>
        Status == DeliveryStatus.Pending && !HasActiveLease(nowUtc)
            ? this with { Status = DeliveryStatus.Cancelled, LeaseUntilUtc = null }
            : null;

    /// <summary>Gives the finish time of a finished item: <see cref="LastTryAtUtc"/>, or <see cref="CreatedAtUtc"/> for an item with no try.</summary>
    /// <returns>The finish time.</returns>
    public DateTime FinishedAtUtc() => LastTryAtUtc ?? CreatedAtUtc;
}
