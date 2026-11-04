using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Management;

/// <summary>
/// The API that lists and changes the subscriptions of all three levels of one tenant (D53, section 4.17). It is a tenant
/// service, on the subscription store and the recipient directory of its key (D116). The consumer protects it with its
/// own authorization. A caller must not await one <see cref="ValueTask"/> two times.
/// </summary>
public interface ISubscriptionManager
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

    /// <summary>
    /// Validates a draft, and adds it as a new subscription with revision 1. An invalid draft gives
    /// <see cref="Errors.ValidationFailed"/>, and a valid draft that conflicts gives <see cref="Errors.Conflict"/>.
    /// </summary>
    /// <param name="draft">The draft.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The new subscription, or an error.</returns>
    ValueTask<ErrorOr<Subscription>> CreateAsync(SubscriptionDraft draft, CancellationToken ct);

    /// <summary>
    /// Validates a draft, and replaces the kind, the channel, the settings, the named users, and the mandatory flag of a
    /// subscription. The owner, the message type, and the recipient kind cannot change. A stored revision that is not
    /// <paramref name="expectedRevision"/> gives <see cref="Errors.Conflict"/> (D79).
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="expectedRevision">The revision that the caller read.</param>
    /// <param name="draft">The draft.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The changed subscription with the next revision, or an error.</returns>
    ValueTask<ErrorOr<Subscription>> UpdateAsync(Guid id, int expectedRevision, SubscriptionDraft draft, CancellationToken ct);

    /// <summary>Deletes a subscription. An unknown id succeeds.</summary>
    /// <param name="id">The id.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or an error.</returns>
    ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct);
}
