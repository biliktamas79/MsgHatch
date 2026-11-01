using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Outbox;

/// <summary>
/// The outbox in memory, for tests, demos, and a host with no database (D119). It is the dispatch store of all
/// tenants, and <see cref="For"/> gives the tenant store of one key. It is not durable: a restart loses each message.
/// </summary>
// ponytail: not durable and for one process, use a storage package for production
public sealed class InMemoryOutboxStore : IOutboxDispatchStore
{
    private readonly Lock _lock = new();
    private readonly List<OutgoingMessage> _messages = [];
    private readonly HashSet<Guid> _ids = [];

    /// <summary>Gives the tenant store of one key. It reads and writes only the messages of its tenant.</summary>
    /// <param name="key">The tenant key.</param>
    /// <returns>The tenant store.</returns>
    public IOutboxStore For(TenantKey key) => new TenantStore(this, key);

    /// <summary>Gives a copy of the stored messages of all tenants, in the order of their insert.</summary>
    /// <returns>The stored messages.</returns>
    public IReadOnlyList<OutgoingMessage> GetMessages()
    {
        lock (_lock)
        {
            return [.. _messages];
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            var deleted = _messages.RemoveAll(static message => message.Flags.HasFlag(MessageFlags.Test));
            _ids.IntersectWith(_messages.Select(static message => message.Delivery.Id));
            return ValueTask.FromResult(ErrorOr<int>.ForValue(deleted));
        }
    }

    private Error? Add(TenantKey key, IReadOnlyList<OutgoingMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Any(message => message.Tenant != key.Tenant))
        {
            return new Error(Errors.ValidationFailed, "A message has another tenant than the key of the store.");
        }

        lock (_lock)
        {
            foreach (var message in messages.Where(message => _ids.Add(message.Delivery.Id)))
            {
                _messages.Add(message);
            }
        }

        return null;
    }

    private sealed class TenantStore(InMemoryOutboxStore store, TenantKey key) : IOutboxStore
    {
        public ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct) =>
            ValueTask.FromResult(store.Add(key, messages));
    }
}
