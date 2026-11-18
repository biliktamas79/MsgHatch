using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Management;

/// <summary>
/// The reads of the subscription management API (D53, D231, section 4.17): the channels, the message types, and the
/// subscriptions of all three levels of one tenant. It is a tenant service, on the subscription store and the recipient
/// directory of its key (D116, D126). The consumer protects it with its own authorization. A caller must not await one
/// <see cref="ValueTask"/> two times.
/// </summary>
public interface ISubscriptionQueries
{
    /// <summary>Gives the descriptor of each registered channel, with its features (D77).</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The channel descriptors, or an error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<ChannelDescriptor>>> GetChannelsAsync(CancellationToken ct);

    /// <summary>
    /// Gives each message type with a scope function, or only <see cref="MessageTypeQuery.Type"/>, with its channels and
    /// its applied subscriptions (D81): for a user, the subscriptions that apply after the merge, and the fixed
    /// subscriptions for named users that name the user. For a company, the subscriptions that apply to its scope users,
    /// its fixed subscriptions, and the default subscriptions for the company. With no filter, the default
    /// subscriptions. A user that the directory does not know gives <see cref="Errors.NotFound"/>.
    /// </summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The message types, or an error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<MessageTypeState>>> GetMessageTypesAsync(MessageTypeQuery query, CancellationToken ct);

    /// <summary>Gives the stored subscriptions that match all given filters.</summary>
    /// <param name="query">The filters. A null filter matches each value.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The subscriptions, or an error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<Subscription>>> GetSubscriptionsAsync(SubscriptionQuery query, CancellationToken ct);

    /// <summary>Gives one subscription, or <see cref="Errors.NotFound"/>.</summary>
    /// <param name="id">The id.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The subscription, or an error.</returns>
    ValueTask<ErrorOr<Subscription>> GetSubscriptionAsync(Guid id, CancellationToken ct);
}
