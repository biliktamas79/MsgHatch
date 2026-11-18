using MsgHatch.Domain.Failures;

namespace MsgHatch.Subscriptions.Management;

/// <summary>
/// The changes of the subscription management API (D53, D231, section 4.17): the create, the update, and the delete of
/// the subscriptions of all three levels of one tenant. It is a tenant service, on the subscription store and the
/// recipient directory of its key (D116, D126). The consumer protects it with its own authorization. A caller must not
/// await one <see cref="ValueTask"/> two times.
/// </summary>
public interface ISubscriptionCommands
{
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
