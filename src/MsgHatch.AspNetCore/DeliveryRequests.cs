using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AspNetCore;

/// <summary>The body of <c>POST {prefix}/channels/{channel}/pause</c> (section 4.19).</summary>
/// <param name="Note">The text of the person, at most 500 characters, or null.</param>
public sealed record PauseRequest(string? Note = null);

/// <summary>The body of <c>POST {prefix}/purge</c> (D128).</summary>
/// <param name="FinishedBefore">The time before which a message must finish, in UTC.</param>
/// <param name="MaxCount">The maximum number of messages of the inbox and of the outbox.</param>
public sealed record PurgeRequest(DateTime FinishedBefore, int MaxCount);

/// <summary>The body of <c>POST {prefix}/inbox/retry</c> and <c>POST {prefix}/inbox/cancel</c>: the filters of the query.</summary>
/// <param name="Ids">The ids, or null.</param>
/// <param name="Status">The status, or null.</param>
/// <param name="MessageType">The message type, or null.</param>
/// <param name="CreatedFrom">The earliest creation time in UTC, included, or null.</param>
/// <param name="CreatedTo">The creation time in UTC that ends the range, not included, or null.</param>
/// <param name="DueBefore">Matches only the pending messages whose next try time is before it, or null.</param>
/// <param name="IncludeTest">True also matches the test messages.</param>
public sealed record InboxFilterRequest(
    IReadOnlyList<Guid>? Ids = null, DeliveryStatus? Status = null, string? MessageType = null, DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null, DateTime? DueBefore = null, bool IncludeTest = false);

/// <summary>The body of <c>POST {prefix}/outbox/retry</c> and <c>POST {prefix}/outbox/cancel</c>: the filters of the query.</summary>
/// <param name="Ids">The ids, or null.</param>
/// <param name="Status">The status, or null.</param>
/// <param name="MessageType">The message type, or null.</param>
/// <param name="Channel">The channel, or null.</param>
/// <param name="TargetKind">The kind of the delivery target. It comes with <paramref name="TargetId"/>, or both are null.</param>
/// <param name="TargetId">The id of the delivery target, or null.</param>
/// <param name="SourceMessageId">The id of the event that gave the message, or null.</param>
/// <param name="CreatedFrom">The earliest creation time in UTC, included, or null.</param>
/// <param name="CreatedTo">The creation time in UTC that ends the range, not included, or null.</param>
/// <param name="DueBefore">Matches only the pending messages whose next try time is before it, or null.</param>
/// <param name="IncludeTest">True also matches the test messages.</param>
public sealed record OutboxFilterRequest(
    IReadOnlyList<Guid>? Ids = null, DeliveryStatus? Status = null, string? MessageType = null, string? Channel = null,
    DeliveryTargetKind? TargetKind = null, string? TargetId = null, Guid? SourceMessageId = null, DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null, DateTime? DueBefore = null, bool IncludeTest = false);
