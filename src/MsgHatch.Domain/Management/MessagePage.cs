using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Management;

/// <summary>One page of messages, newest first (D112).</summary>
/// <typeparam name="T">The summary type of a message.</typeparam>
/// <param name="Items">The messages of the page.</param>
/// <param name="Next">The cursor of the last message, or null when the page has fewer messages than the limit.</param>
public sealed record MessagePage<T>(IReadOnlyList<T> Items, MessageCursor? Next);

/// <summary>Makes a <see cref="MessagePage{T}"/> for a store that reads the messages into memory.</summary>
public static class MessagePage
{
    /// <summary>Sorts the messages newest first, starts after the cursor, and takes at most <paramref name="limit"/> messages.</summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <typeparam name="T">The summary type.</typeparam>
    /// <param name="messages">The messages that match the filters.</param>
    /// <param name="delivery">Gives the delivery state of a message.</param>
    /// <param name="summary">Gives the summary of a message.</param>
    /// <param name="after">The cursor of the last message of the previous page, or null.</param>
    /// <param name="limit">The maximum number of messages.</param>
    /// <returns>The page.</returns>
    public static MessagePage<T> Of<TMessage, T>(
        IEnumerable<TMessage> messages, Func<TMessage, DeliveryState> delivery, Func<TMessage, T> summary, MessageCursor? after,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        TMessage[] page =
        [
            .. messages
                .Where(message => after is not { } cursor || cursor.Precedes(delivery(message)))
                .OrderByDescending(message => delivery(message).CreatedAtUtc)
                .ThenByDescending(message => delivery(message).Id)
                .Take(limit),
        ];
        return new MessagePage<T>(
            [.. page.Select(summary)],
            page.Length > 0 && page.Length == limit ? MessageCursor.Of(delivery(page[^1])) : null);
    }
}
