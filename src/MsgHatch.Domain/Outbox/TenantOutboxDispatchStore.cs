using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Domain.Outbox;

/// <summary>
/// The outbox dispatch store of a setup with one storage for each tenant (D125, section 4.5). A claim asks the store of
/// <see cref="TenantKey.None"/>, which keeps the down states of the channels, and then the stores of the tenants of the
/// tenant source in turn. A try goes to the store of the tenant of the message. <c>UseStorageForEachTenant</c>
/// registers it.
/// </summary>
public sealed class TenantOutboxDispatchStore : IOutboxDispatchStore
{
    private readonly TenantDispatch<IOutboxDispatchStore> _dispatch;

    internal TenantOutboxDispatchStore(
        ITenantSource tenants, Func<TenantKey, IOutboxDispatchStore> storeOf, TimeSpan maxPollInterval, ILogger logger)
    {
        _dispatch = new TenantDispatch<IOutboxDispatchStore>(tenants, storeOf, maxPollInterval, logger);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimDueAsync(
        ChannelId channel, int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        _dispatch.ClaimAsync(channel.Name, maxCount, nowUtc, (store, count) => store.ClaimDueAsync(channel, count, nowUtc, lease, ct), ct);

    /// <inheritdoc />
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> RecordTryAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var key = TenantKey.For(message.Tenant);
        var error = await _dispatch.StoreOf(key).RecordTryAsync(message, publishTry, nextTryAtUtc, ct);
        if (error is null && nextTryAtUtc is not null)
        {
            _dispatch.ForgetNextDue(key);
        }

        return error;
    }

    /// <inheritdoc />
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct)
    {
        var counts = await _dispatch.CollectAsync(store => store.CountAsync(query, ct), ct);
        return counts.IsError ? counts.Error : ErrorOr<IReadOnlyList<MessageCount>>.ForValue(Sum(counts.Value));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        _dispatch.DeleteAsync(int.MaxValue, (store, _) => store.DeleteTestDataAsync(ct), ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        _dispatch.DeleteAsync(maxCount, (store, left) => store.PurgeAsync(finishedBeforeUtc, left, ct), ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct) =>
        SystemStore.GetDownChannelsAsync(ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct) =>
        SystemStore.SetChannelDownAsync(down, ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, CancellationToken ct) =>
        SystemStore.SetChannelUpAsync(channel, reason, ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct) =>
        SystemStore.TryStartChannelCheckAsync(channel, nowUtc, nextCheckAtUtc, ct);

    /// <summary>
    /// Puts the internal wrapper around the tenant store of a key: after a successful <c>AddAsync</c> or
    /// <c>RetryAsync</c>, the next claim of each channel asks that tenant again.
    /// </summary>
    internal IOutboxStore TellingNewMessages(IOutboxStore store, TenantKey key) => new NewMessagesTeller(store, key, _dispatch);

    private IOutboxDispatchStore SystemStore => _dispatch.StoreOf(TenantKey.None);

    private static IReadOnlyList<MessageCount> Sum(IEnumerable<MessageCount> counts) =>
    [
        .. counts
            .GroupBy(static count => (count.Type, count.Channel, count.Status, count.Test))
            .Select(static group => new MessageCount(
                group.Key.Type, group.Key.Channel, group.Key.Status, group.Key.Test, group.Sum(static count => count.Count),
                group.Min(static count => count.OldestDueAtUtc))),
    ];

    private sealed class NewMessagesTeller(IOutboxStore inner, TenantKey key, TenantDispatch<IOutboxDispatchStore> dispatch) : IOutboxStore
    {
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        public async ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct)
        {
            var error = await inner.AddAsync(messages, ct);
            if (error is null)
            {
                dispatch.ForgetNextDue(key);
            }

            return error;
        }

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        public async ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct)
        {
            var retried = await inner.RetryAsync(ids, nowUtc, addedTries, ct);
            if (!retried.IsError)
            {
                dispatch.ForgetNextDue(key);
            }

            return retried;
        }

        public ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct) =>
            inner.QueryAsync(query, ct);

        public ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
            inner.CountAsync(query, ct);

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            inner.CancelAsync(ids, nowUtc, ct);
    }
}
