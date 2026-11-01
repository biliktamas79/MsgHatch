using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// The down state of one channel in the outbox dispatch store, for all publisher instances of the channel (D106).
/// A channel with no down state is up. All times are UTC.
/// </summary>
/// <param name="Channel">The channel.</param>
/// <param name="Reason">Why the channel is down.</param>
/// <param name="SinceUtc">The start of the down state.</param>
/// <param name="NextCheckAtUtc">The time of the next health check, only for the reason <see cref="ChannelDownReason.Errors"/>.</param>
/// <param name="ErrorCode">The code of the last transient error, only for the reason <see cref="ChannelDownReason.Errors"/>.</param>
/// <param name="ErrorName">The name of the last transient error, only for the reason <see cref="ChannelDownReason.Errors"/>.</param>
/// <param name="Note">The text of the person, at most 500 characters, only for the reason <see cref="ChannelDownReason.Operator"/>.</param>
public sealed record ChannelDown(
    ChannelId Channel, ChannelDownReason Reason, DateTime SinceUtc, DateTime? NextCheckAtUtc,
    int? ErrorCode, string? ErrorName, string? Note);
