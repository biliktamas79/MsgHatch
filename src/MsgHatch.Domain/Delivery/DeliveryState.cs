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
    string? LastError);
