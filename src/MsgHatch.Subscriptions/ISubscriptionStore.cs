using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The store of the subscriptions of all three levels of one tenant (D78, D116). It is a tenant service: the store of a
/// key holds only the subscriptions of its tenant. Each method returns an error for a failure and does not throw. A caller
/// must not await one <see cref="ValueTask"/> two times.
/// </summary>
public interface ISubscriptionStore
{
    /// <summary>
    /// Gives, for one message type, the default subscriptions, the subscriptions of the given companies, and the
    /// subscriptions of the given users, in one call. It does no resolution.
    /// </summary>
    /// <param name="type">The message type.</param>
    /// <param name="companies">The companies.</param>
    /// <param name="users">The users.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The subscriptions, or an error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
        MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct);

    /// <summary>Gives the subscriptions that match all given filters. A null filter matches each value.</summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The subscriptions, or an error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct);

    /// <summary>Gives one subscription, or null for an unknown id.</summary>
    /// <param name="id">The id.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The subscription, null, or an error.</returns>
    ValueTask<ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Adds a subscription. A second subscription with the same owner, message type, channel, and recipient kind gives
    /// <see cref="Errors.Conflict"/>.
    /// </summary>
    /// <param name="subscription">The subscription.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or an error.</returns>
    ValueTask<Error?> AddAsync(Subscription subscription, CancellationToken ct);

    /// <summary>
    /// Replaces a subscription. A stored revision that is not <paramref name="expectedRevision"/> gives
    /// <see cref="Errors.Conflict"/>, and an unknown id gives <see cref="Errors.NotFound"/>.
    /// </summary>
    /// <param name="subscription">The new state of the subscription.</param>
    /// <param name="expectedRevision">The revision that the store must have.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or an error.</returns>
    ValueTask<Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct);

    /// <summary>Deletes a subscription. An unknown id succeeds.</summary>
    /// <param name="id">The id.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or an error.</returns>
    ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct);
}
