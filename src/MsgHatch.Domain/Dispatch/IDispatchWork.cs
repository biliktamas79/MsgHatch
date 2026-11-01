using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// What one dispatch loop claims and does for each message: the inbox, or the outbox of one channel (section 4.9).
/// A member returns an <see cref="Error"/> or an <see cref="ItemResult"/> for a failure, and does not throw.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
internal interface IDispatchWork<T>
{
    DispatchTopic Topic { get; }

    ValueTask BeforeClaimAsync(CancellationToken ct);

    ValueTask<ErrorOr<ClaimResult<T>>> ClaimAsync(int maxCount, DateTime nowUtc, CancellationToken ct);

    /// <summary>The time at which the claim loop claims again with no signal, or null to wait only for a signal or a poll.</summary>
    DateTime? NextWakeAtUtc(ClaimResult<T> result);

    DateTime? LeaseUntilUtcOf(T message);

    ValueTask<ItemResult> ProcessAsync(T message, CancellationToken ct);
}

/// <summary>What a processed message counts as in a <see cref="DispatchSummary"/>.</summary>
internal enum ItemOutcome
{
    None,
    Succeeded,
    Retried,
    Failed,
}

/// <summary>The outcome of one processed message, and the time that the worker gives to its claim loop, or null (D103).</summary>
internal readonly record struct ItemResult(ItemOutcome Outcome, DateTime? GiveAtUtc)
{
    public static ItemResult None { get; } = new(ItemOutcome.None, GiveAtUtc: null);
}
