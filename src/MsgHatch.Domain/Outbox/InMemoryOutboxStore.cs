using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
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
    private readonly Dictionary<Guid, List<PublishTry>> _tries = [];
    private readonly Dictionary<ChannelId, ChannelDown> _downChannels = [];

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
                _downChannels.TryGetValue(channel, out var down)
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
            foreach (var id in _tries.Keys.Where(id => !_ids.Contains(id)).ToArray())
            {
                _tries.Remove(id);
            }

            return ValueTask.FromResult(ErrorOr<int>.ForValue(deleted));
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([.. _downChannels.Values]));
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(down);
        lock (_lock)
        {
            var firstLoopWins = down.Reason == ChannelDownReason.Errors && _downChannels.ContainsKey(down.Channel);
            if (!firstLoopWins)
            {
                _downChannels[down.Channel] = down;
            }

            return ValueTask.FromResult(ErrorOr<bool>.ForValue(!firstLoopWins));
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, CancellationToken ct)
    {
        lock (_lock)
        {
            var ends = _downChannels.TryGetValue(channel, out var down) && (reason is null || down.Reason == reason);
            if (ends)
            {
                _downChannels.Remove(channel);
            }

            return ValueTask.FromResult(ErrorOr<bool>.ForValue(ends));
        }
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct)
    {
        lock (_lock)
        {
            var due = _downChannels.TryGetValue(channel, out var down)
                && down.Reason == ChannelDownReason.Errors
                && down.NextCheckAtUtc <= nowUtc;
            if (due)
            {
                _downChannels[channel] = down! with { NextCheckAtUtc = nextCheckAtUtc };
            }

            return ValueTask.FromResult(ErrorOr<bool>.ForValue(due));
        }
    }

    private ClaimResult<OutgoingMessage> Claim(ChannelId channel, int maxCount, DateTime nowUtc, DateTime leaseUntilUtc)
    {
        var due = _messages
            .Select(static (message, position) => (message, position))
            .Where(item => IsPendingOn(item.message, channel) && item.message.Delivery.NextTryAtUtc <= nowUtc)
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
    }
}
