using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Inbox;

/// <summary>
/// The inbox in memory, for tests, demos, and a host with no database (D119). It is the dispatch store of all
/// tenants, and <see cref="For"/> gives the tenant store of one key. It is not durable: a restart loses each message.
/// </summary>
// ponytail: not durable and for one process, use a storage package for production
public sealed class InMemoryInboxStore : IInboxDispatchStore
{
    private readonly Lock _lock = new();
    private readonly List<ReceivedMessage> _messages = [];
    private readonly HashSet<(TenantId? Tenant, Guid Id)> _keys = [];

    /// <summary>Gives the tenant store of one key. It reads and writes only the messages of its tenant.</summary>
    /// <param name="key">The tenant key.</param>
    /// <returns>The tenant store.</returns>
    public IInboxStore For(TenantKey key) => new TenantStore(this, key);

    /// <summary>Gives a copy of the stored messages of all tenants, in the order of their insert.</summary>
    /// <returns>The stored messages.</returns>
    public IReadOnlyList<ReceivedMessage> GetMessages()
    {
        lock (_lock)
        {
            return [.. _messages];
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimDueAsync(
        int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct)
    {
        if (maxCount < 1)
        {
            return ValueTask.FromResult<ErrorOr<ClaimResult<ReceivedMessage>>>(
                new Error(Errors.ValidationFailed, "maxCount is less than 1."));
        }

        lock (_lock)
        {
            return ValueTask.FromResult(ErrorOr<ClaimResult<ReceivedMessage>>.ForValue(Claim(maxCount, nowUtc, nowUtc + lease)));
        }
    }

    /// <inheritdoc />
    public ValueTask<Error?> CompleteAsync(ReceivedMessage message, DateTime triedAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(Update(message, delivery => delivery with
        {
            Status = DeliveryStatus.Done,
            TryCount = delivery.TryCount + 1,
            LastTryAtUtc = triedAtUtc,
            LeaseUntilUtc = null,
        }));

    /// <inheritdoc />
    public ValueTask<Error?> FailAsync(
        ReceivedMessage message, string error, DateTime triedAtUtc, DateTime? nextTryAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(Update(message, delivery => delivery with
        {
            Status = nextTryAtUtc is null ? DeliveryStatus.Failed : DeliveryStatus.Pending,
            TryCount = delivery.TryCount + 1,
            LastTryAtUtc = triedAtUtc,
            NextTryAtUtc = nextTryAtUtc ?? delivery.NextTryAtUtc,
            LeaseUntilUtc = null,
            LastError = error,
        }));

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            var deleted = _messages.RemoveAll(static message => message.Flags.HasFlag(MessageFlags.Test));
            _keys.IntersectWith(_messages.Select(static message => KeyOf(message)));
            return ValueTask.FromResult(ErrorOr<int>.ForValue(deleted));
        }
    }

    private ClaimResult<ReceivedMessage> Claim(int maxCount, DateTime nowUtc, DateTime leaseUntilUtc)
    {
        var due = _messages
            .Select(static (message, position) => (message, position))
            .Where(item => item.message.Delivery.Status == DeliveryStatus.Pending && item.message.Delivery.NextTryAtUtc <= nowUtc)
            .OrderBy(static item => item.message.Delivery.NextTryAtUtc)
            .Take(maxCount)
            .Select(static item => item.position)
            .ToArray();
        foreach (var position in due)
        {
            var message = _messages[position];
            _messages[position] = message with
            {
                Delivery = message.Delivery with { NextTryAtUtc = leaseUntilUtc, LeaseUntilUtc = leaseUntilUtc },
            };
        }

        var nextDueAtUtc = _messages
            .Where(static message => message.Delivery.Status == DeliveryStatus.Pending)
            .Select(static message => (DateTime?)message.Delivery.NextTryAtUtc)
            .Min();
        return new ClaimResult<ReceivedMessage>([.. due.Select(position => _messages[position])], nextDueAtUtc);
    }

    private Error? Update(ReceivedMessage message, Func<DeliveryState, DeliveryState> change)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_lock)
        {
            var position = _messages.FindIndex(stored => KeyOf(stored) == KeyOf(message));
            if (position < 0)
            {
                return new Error(Errors.NotFound, "The received message is not stored.");
            }

            _messages[position] = _messages[position] with { Delivery = change(_messages[position].Delivery) };
            return null;
        }
    }

    private Error? Add(TenantKey key, ReceivedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Tenant != key.Tenant)
        {
            return new Error(Errors.ValidationFailed, "The message has another tenant than the key of the store.");
        }

        lock (_lock)
        {
            if (_keys.Add(KeyOf(message)))
            {
                _messages.Add(message);
            }
        }

        return null;
    }

    private static (TenantId? Tenant, Guid Id) KeyOf(ReceivedMessage message) => (message.Tenant, message.Delivery.Id);

    private sealed class TenantStore(InMemoryInboxStore store, TenantKey key) : IInboxStore
    {
        public ValueTask<Error?> AddAsync(ReceivedMessage message, CancellationToken ct) =>
            ValueTask.FromResult(store.Add(key, message));
    }
}
