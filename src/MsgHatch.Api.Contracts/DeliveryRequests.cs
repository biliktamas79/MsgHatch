using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Api.Contracts;

/// <summary>The body of <c>POST {prefix}/channels/{channel}/pause</c> (section 4.19, D291).</summary>
/// <param name="Note">The text of the person, at most 500 characters, or null.</param>
public sealed record PauseRequest(string? Note = null);

/// <summary>The body of <c>POST {prefix}/purge</c> (D128, D291).</summary>
/// <param name="FinishedBefore">The time before which a message must finish, in UTC.</param>
/// <param name="MaxCount">The maximum number of messages of the inbox and of the outbox.</param>
public sealed record PurgeRequest(DateTime FinishedBefore, int MaxCount);

/// <summary>The body of <c>POST {prefix}/inbox/retry</c> and <c>POST {prefix}/inbox/cancel</c>: the filters of the query (D291).</summary>
/// <param name="Ids">The ids, or null.</param>
/// <param name="Status">The status, or null.</param>
/// <param name="MessageType">The message type, or null.</param>
/// <param name="CreatedFrom">The earliest creation time in UTC, included, or null.</param>
/// <param name="CreatedTo">The creation time in UTC that ends the range, not included, or null.</param>
/// <param name="DueBefore">Matches only the pending messages whose next try time is before it, or null.</param>
/// <param name="IncludeTest">True also matches the test messages.</param>
public sealed record InboxFilterRequest(
    IReadOnlyList<Guid>? Ids = null, DeliveryStatus? Status = null, string? MessageType = null, DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null, DateTime? DueBefore = null, bool IncludeTest = false)
{
    /// <summary>
    /// Gives the body of a retry or a cancel of the inbox (D228). A bulk action reads its messages page by page, so the body
    /// has no limit and no cursor.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The query is null.</exception>
    public static InboxFilterRequest From(InboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new InboxFilterRequest(
            query.Ids, query.Status, query.Type?.Name, query.CreatedFromUtc, query.CreatedToUtc, query.DueBeforeUtc, query.IncludeTest);
    }
}

/// <summary>The body of <c>POST {prefix}/outbox/retry</c> and <c>POST {prefix}/outbox/cancel</c>: the filters of the query (D291).</summary>
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
    DateTime? CreatedTo = null, DateTime? DueBefore = null, bool IncludeTest = false)
{
    /// <summary>
    /// Gives the body of a retry or a cancel of the outbox (D228). A bulk action reads its messages page by page, so the body
    /// has no limit and no cursor.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The query is null.</exception>
    public static OutboxFilterRequest From(OutboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new OutboxFilterRequest(
            query.Ids, query.Status, query.Type?.Name, query.Channel?.Name, query.Target?.Kind, query.Target?.Id, query.SourceMessageId,
            query.CreatedFromUtc, query.CreatedToUtc, query.DueBeforeUtc, query.IncludeTest);
    }
}
