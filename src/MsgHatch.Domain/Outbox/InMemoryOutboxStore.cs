using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

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
    private readonly Dictionary<Guid, List<PublishTry>> _tries = [];
    private readonly Dictionary<ChannelId, List<ChannelState>> _channelStates = [];

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

    /// <summary>Gives a copy of the publish tries of one stored message, in the order of their record.</summary>
    /// <param name="outgoingMessageId">The id of the outgoing message.</param>
    /// <returns>The tries. An unknown id gives no tries.</returns>
    public IReadOnlyList<PublishTry> GetTries(Guid outgoingMessageId)
    {
        lock (_lock)
        {
            return _tries.TryGetValue(outgoingMessageId, out var tries) ? [.. tries] : [];
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimDueAsync(
        ChannelId channel, int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct)
    {
        if (maxCount < 1)
        {
            return ValueTask.FromResult<ErrorOr<ClaimResult<OutgoingMessage>>>(
                new Error(Errors.ValidationFailed, "maxCount is less than 1."));
        }

        lock (_lock)
        {
            return ValueTask.FromResult(ErrorOr<ClaimResult<OutgoingMessage>>.ForValue(
                LatestStateOf(channel)?.ToDown() is { } down
                    ? new ClaimResult<OutgoingMessage>([], NextDueAtUtc: null, down)
                    : Claim(channel, maxCount, nowUtc, nowUtc + lease)));
        }
    }

    /// <inheritdoc />
    public ValueTask<Error?> RecordTryAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publishTry);
        lock (_lock)
        {
            var position = _messages.FindIndex(stored => stored.Delivery.Id == message.Delivery.Id);
            if (position < 0)
            {
                return ValueTask.FromResult<Error?>(new Error(Errors.NotFound, "The outgoing message is not stored."));
            }

            var stored = _messages[position];
            if (stored.Delivery.TryCount == publishTry.TryNumber - 1)
            {
                _messages[position] = stored with { Delivery = AfterTry(stored.Delivery, publishTry, nextTryAtUtc) };
                TriesOf(stored.Delivery.Id).Add(publishTry);
            }

            return ValueTask.FromResult<Error?>(null);
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            var deleted = _messages.RemoveAll(static message => message.Flags.HasFlag(MessageFlags.Test));
            _ids.IntersectWith(_messages.Select(static message => message.Delivery.Id));
            Guid[] orphanTries = [.. _tries.Keys.Where(id => !_ids.Contains(id))];
            foreach (var id in orphanTries)
            {
                _tries.Remove(id);
            }

            return ValueTask.FromResult(ErrorOr<int>.ForValue(deleted));
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue(Count(query, tenant: null)));

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct)
    {
        if (maxCount < 1)
        {
            return ValueTask.FromResult<ErrorOr<int>>(new Error(Errors.ValidationFailed, "maxCount is less than 1."));
        }

        lock (_lock)
        {
            HashSet<Guid> purged =
            [
                .. _messages
                    .Where(message => message.Delivery.FinishedBefore(finishedBeforeUtc))
                    .OrderBy(static message => message.Delivery.FinishedAtUtc())
                    .Take(maxCount)
                    .Select(static message => message.Delivery.Id),
            ];
            _messages.RemoveAll(message => purged.Contains(message.Delivery.Id));
            _ids.ExceptWith(purged);
            foreach (var id in purged)
            {
                _tries.Remove(id);
            }

            return ValueTask.FromResult(ErrorOr<int>.ForValue(purged.Count));
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue(
                [.. _channelStates.Values.Select(static rows => rows[^1].ToDown()).OfType<ChannelDown>()]));
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(down);
        lock (_lock)
        {
            return ValueTask.FromResult(ChannelState.Change(() => LatestStateOf(down.Channel), latest => ChannelState.AfterDown(latest, down), AddState));
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, DateTime upAtUtc, CancellationToken ct)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(
                ChannelState.Change(() => LatestStateOf(channel), latest => ChannelState.AfterUp(latest, channel, reason, upAtUtc), AddState));
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelStatesAsync(DateTime fromUtc, CancellationToken ct)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelState>>.ForValue(
                ChannelState.NewestFirst(_channelStates.Values.SelectMany(static rows => rows), fromUtc)));
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct)
    {
        lock (_lock)
        {
            var latest = LatestStateOf(channel);
            var due = latest is { Kind: ChannelStateKind.Down, Reason: ChannelDownReason.Errors } && latest.NextCheckAtUtc <= nowUtc;
            if (due)
            {
                var rows = _channelStates[channel];
                rows[^1] = latest! with { NextCheckAtUtc = nextCheckAtUtc };
            }

            return ValueTask.FromResult(ErrorOr<bool>.ForValue(due));
        }
    }

    private MessagePage<OutgoingMessageSummary> Query(TenantKey key, OutboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_lock)
        {
            return MessagePage.Of(
                _messages.Where(message => message.Tenant == key.Tenant && query.Matches(message)),
                static message => message.Delivery, OutgoingMessageSummary.Of, query.After, query.Limit);
        }
    }

    private OutgoingMessageHistory? Get(TenantKey key, Guid id)
    {
        lock (_lock)
        {
            return _messages.Find(message => message.Delivery.Id == id && message.Tenant == key.Tenant) is { } message
                ? new OutgoingMessageHistory(message, _tries.TryGetValue(id, out var tries) ? [.. tries] : [])
                : null;
        }
    }

    /// <summary>Changes each message of the tenant with one of the ids, when the change gives a new message.</summary>
    private int Change(TenantKey key, IReadOnlyList<Guid> ids, Func<OutgoingMessage, OutgoingMessage?> change)
    {
        ArgumentNullException.ThrowIfNull(ids);
        lock (_lock)
        {
            var changed = 0;
            for (var position = 0; position < _messages.Count; position++)
            {
                var message = _messages[position];
                if (message.Tenant == key.Tenant && ids.Contains(message.Delivery.Id) && change(message) is { } result)
                {
                    _messages[position] = result;
                    changed++;
                }
            }

            return changed;
        }
    }

    /// <summary>Cancels a message, and records the cancel as a publish try with the next try number (D110).</summary>
    private OutgoingMessage? Cancelled(OutgoingMessage message, DateTime nowUtc)
    {
        if (message.Delivery.CancelledAt(nowUtc) is not { } cancelled)
        {
            return null;
        }

        var tryNumber = message.Delivery.TryCount + 1;
        TriesOf(message.Delivery.Id).Add(PublishTry.Cancel(message.Delivery.Id, tryNumber, nowUtc));
        return message with { Delivery = cancelled with { TryCount = tryNumber, LastTryAtUtc = nowUtc } };
    }

    private IReadOnlyList<MessageCount> Count(TrafficQuery query, TenantKey? tenant)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_lock)
        {
            return MessageCount.Of(_messages
                .Where(message => (tenant is null || message.Tenant == tenant.Value.Tenant) && query.Matches(message.Delivery, message.Flags))
                .Select(static message => (message.Type, (ChannelId?)message.Channel, message.Delivery, message.Flags)));
        }
    }

    private ChannelState? LatestStateOf(ChannelId channel) => _channelStates.TryGetValue(channel, out var rows) ? rows[^1] : null;

    private bool AddState(ChannelState row)
    {
        if (!_channelStates.TryGetValue(row.Channel, out var rows))
        {
            _channelStates[row.Channel] = rows = [];
        }

        rows.Add(row);
        return true;
    }

    private ClaimResult<OutgoingMessage> Claim(ChannelId channel, int maxCount, DateTime nowUtc, DateTime leaseUntilUtc)
    {
        int[] due =
        [
            .. _messages
                .Select(static (message, position) => (message, position))
                .Where(item => IsPendingOn(item.message, channel) && item.message.Delivery.NextTryAtUtc <= nowUtc)
                .OrderBy(static item => item.message.Delivery.NextTryAtUtc)
                .Take(maxCount)
                .Select(static item => item.position),
        ];
        foreach (var position in due)
        {
            var message = _messages[position];
            _messages[position] = message with
            {
                Delivery = message.Delivery with { NextTryAtUtc = leaseUntilUtc, LeaseUntilUtc = leaseUntilUtc },
            };
        }

        return new ClaimResult<OutgoingMessage>([.. due.Select(position => _messages[position])], NextDueAtUtc(channel));
    }

    private DateTime? NextDueAtUtc(ChannelId channel) =>
        _messages
            .Where(message => IsPendingOn(message, channel))
            .Select(static message => (DateTime?)message.Delivery.NextTryAtUtc)
            .Min();

    private static bool IsPendingOn(OutgoingMessage message, ChannelId channel) =>
        message.Channel == channel && message.Delivery.Status == DeliveryStatus.Pending;

    private static DeliveryState AfterTry(DeliveryState delivery, PublishTry publishTry, DateTime? nextTryAtUtc)
    {
        var status = publishTry.StatusAfter(nextTryAtUtc);
        return delivery with
        {
            Status = status,
            TryCount = publishTry.TryNumber,
            LastTryAtUtc = publishTry.FinishedAtUtc,
            NextTryAtUtc = status == DeliveryStatus.Pending ? nextTryAtUtc!.Value : delivery.NextTryAtUtc,
            LeaseUntilUtc = null,
            LastError = publishTry.ErrorText(),
        };
    }

    private List<PublishTry> TriesOf(Guid id)
    {
        if (!_tries.TryGetValue(id, out var tries))
        {
            tries = [];
            _tries.Add(id, tries);
        }

        return tries;
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

        public ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(store.Query(key, query)));

        public ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(store.Get(key, id)));

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue(store.Count(query, key)));

        public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<int>.ForValue(store.Change(
                key, ids, message => message.Delivery.RetriedAt(nowUtc, addedTries) is { } delivery ? message with { Delivery = delivery } : null)));

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<int>.ForValue(store.Change(key, ids, message => store.Cancelled(message, nowUtc))));
    }
}
