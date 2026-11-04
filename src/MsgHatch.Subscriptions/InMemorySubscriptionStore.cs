using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>
/// A subscription store in memory, for a host with no database. It is not durable. <c>UseInMemorySubscriptionStore</c>
/// gives each tenant key its own instance (D116, D126). It is thread-safe.
/// </summary>
public sealed class InMemorySubscriptionStore : ISubscriptionStore
{
    private readonly Lock _lock = new();
    private readonly List<Subscription> _subscriptions = [];

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
        MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(users);
        return Matching(subscription => subscription.Type == type && subscription.Owner.Level switch
        {
            SubscriptionLevel.Default => true,
            SubscriptionLevel.Company => companies.Contains(subscription.Owner.Company!.Value),
            _ => users.Contains(subscription.Owner.User!.Value),
        });
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Matching(subscription =>
            (query.Type is null || subscription.Type == query.Type)
            && (query.Company is null || subscription.Owner.Company == query.Company)
            && (query.User is null || subscription.Owner.User == query.User)
            && (query.Level is null || subscription.Owner.Level == query.Level));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(ErrorOr<Subscription?>.ForValue(_subscriptions.Find(subscription => subscription.Id == id)));
        }
    }

    /// <inheritdoc />
    public ValueTask<Error?> AddAsync(Subscription subscription, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        lock (_lock)
        {
            if (HasSameOwnerTypeChannelAndRecipients(subscription))
            {
                return ValueTask.FromResult<Error?>(SameOwnerTypeChannelAndRecipients());
            }

            _subscriptions.Add(subscription);
            return ValueTask.FromResult<Error?>(null);
        }
    }

    /// <inheritdoc />
    public ValueTask<Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        lock (_lock)
        {
            var index = _subscriptions.FindIndex(stored => stored.Id == subscription.Id);
            Error? error =
                index < 0 ? new Error(Errors.NotFound, "The subscription is not stored.")
                : _subscriptions[index].Revision != expectedRevision ? new Error(Errors.Conflict, "The stored revision is not the expected revision.")
                : HasSameOwnerTypeChannelAndRecipients(subscription) ? SameOwnerTypeChannelAndRecipients()
                : null;
            if (error is null)
            {
                _subscriptions[index] = subscription;
            }

            return ValueTask.FromResult(error);
        }
    }

    /// <inheritdoc />
    public ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct)
    {
        lock (_lock)
        {
            _subscriptions.RemoveAll(subscription => subscription.Id == id);
            return ValueTask.FromResult<Error?>(null);
        }
    }

    private ValueTask<ErrorOr<IReadOnlyList<Subscription>>> Matching(Predicate<Subscription> match)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(ErrorOr<IReadOnlyList<Subscription>>.ForValue(_subscriptions.FindAll(match)));
        }
    }

    private bool HasSameOwnerTypeChannelAndRecipients(Subscription subscription) =>
        _subscriptions.Exists(stored =>
            stored.Id != subscription.Id
            && stored.Owner == subscription.Owner
            && stored.Type == subscription.Type
            && stored.Channel == subscription.Channel
            && stored.Recipients.Kind == subscription.Recipients.Kind);

    private static Error SameOwnerTypeChannelAndRecipients() =>
        new(Errors.Conflict, "The owner already has a subscription for the message type, the channel, and the recipient kind.");
}
