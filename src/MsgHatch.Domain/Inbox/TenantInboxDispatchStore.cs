using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Domain.Inbox;

/// <summary>
/// The inbox dispatch store of a setup with one storage for each tenant (D125, section 4.5). A claim asks the store of
/// <see cref="TenantKey.None"/>, and then the stores of the tenants of the tenant source in turn. A completion or a
/// failure goes to the store of the tenant of the message. <c>UseStorageForEachTenant</c> registers it.
/// </summary>
public sealed class TenantInboxDispatchStore : IInboxDispatchStore
{
    private const string _inboxLane = "";

    private readonly TenantDispatch<IInboxDispatchStore> _dispatch;

    internal TenantInboxDispatchStore(
        ITenantSource tenants, Func<TenantKey, IInboxDispatchStore> storeOf, TimeSpan maxPollInterval, ILogger logger)
    {
        _dispatch = new TenantDispatch<IInboxDispatchStore>(tenants, storeOf, maxPollInterval, logger);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimDueAsync(
        int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        _dispatch.ClaimAsync(_inboxLane, maxCount, nowUtc, (store, count) => store.ClaimDueAsync(count, nowUtc, lease, ct), ct);

    /// <inheritdoc />
    public ValueTask<Error?> CompleteAsync(ReceivedMessage message, DateTime triedAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _dispatch.StoreOf(TenantKey.For(message.Tenant)).CompleteAsync(message, triedAtUtc, ct);
    }

    /// <inheritdoc />
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> FailAsync(
        ReceivedMessage message, string error, DateTime triedAtUtc, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var key = TenantKey.For(message.Tenant);
        var failure = await _dispatch.StoreOf(key).FailAsync(message, error, triedAtUtc, nextTryAtUtc, ct);
        if (failure is null && nextTryAtUtc is not null)
        {
            _dispatch.ForgetNextDue(key);
        }

        return failure;
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        _dispatch.DeleteAsync(int.MaxValue, (store, _) => store.DeleteTestDataAsync(ct), ct);

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        _dispatch.DeleteAsync(maxCount, (store, left) => store.PurgeAsync(finishedBeforeUtc, left, ct), ct);

    /// <summary>
    /// Puts the internal wrapper around the tenant store of a key: after a successful <c>AddAsync</c> or
    /// <c>RetryAsync</c>, the next claim asks that tenant again.
    /// </summary>
    internal IInboxStore TellingNewMessages(IInboxStore store, TenantKey key) => new NewMessagesTeller(store, key, _dispatch);

    private sealed class NewMessagesTeller(IInboxStore inner, TenantKey key, TenantDispatch<IInboxDispatchStore> dispatch) : IInboxStore
    {
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        public async ValueTask<Error?> AddAsync(ReceivedMessage message, CancellationToken ct)
        {
            var error = await inner.AddAsync(message, ct);
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

        public ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> QueryAsync(InboxQuery query, CancellationToken ct) =>
            inner.QueryAsync(query, ct);

        public ValueTask<ErrorOr<ReceivedMessage?>> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
            inner.CountAsync(query, ct);

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            inner.CancelAsync(ids, nowUtc, ct);
    }
}
