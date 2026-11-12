using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Caching;

/// <summary>
/// The caching wrapper of a subscription store (section 4.15, D38). It caches the subscriptions of one message type and
/// one owner in one entry, also an empty list, and reads all missing owners with one store call. After each change
/// through the store, it removes the entries of the owner of the change. A backend error never fails a call.
/// </summary>
internal sealed class SubscriptionStoreCachingWrapper(
    ISubscriptionStore inner,
    ISubscriptionCacheBackend backend,
    SubscriptionCacheKeys keys,
    TimeSpan expiration,
    TenantKey key,
    ILogger<SubscriptionStoreCachingWrapper> logger)
    : ISubscriptionStore
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
        MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(users);
        var found = new List<Subscription>();
        var missing = new List<SubscriptionOwner>();
        await ReadAsync(type, SubscriptionOwner.Default, found, missing, ct);
        foreach (var company in companies)
        {
            await ReadAsync(type, SubscriptionOwner.ForCompany(company), found, missing, ct);
        }

        foreach (var user in users)
        {
            await ReadAsync(type, SubscriptionOwner.ForUser(user), found, missing, ct);
        }

        Log.FindForDeliveryCached(logger, 1 + companies.Count + users.Count - missing.Count, missing.Count, type.Name, key.Tenant?.Value);
        return missing.Count == 0 ? found : await ReadMissingOwnersAsync(type, missing, found, ct);
    }

    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct) => inner.QueryAsync(query, ct);

    public ValueTask<ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> AddAsync(Subscription subscription, CancellationToken ct)
    {
        var error = await inner.AddAsync(subscription, ct);
        if (error is null)
        {
            await RemoveOwnerAsync(subscription.Owner, ct);
        }

        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct)
    {
        var error = await inner.UpdateAsync(subscription, expectedRevision, ct);
        if (error is null)
        {
            await RemoveOwnerAsync(subscription.Owner, ct);
        }

        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct)
    {
        var stored = await inner.GetAsync(id, ct);
        if (stored.IsError)
        {
            return stored.Error;
        }

        var error = await inner.DeleteAsync(id, ct);
        if (error is null && stored.Value is { } subscription)
        {
            await RemoveOwnerAsync(subscription.Owner, ct);
        }

        return error;
    }

    private async ValueTask ReadAsync(
        MessageType type, SubscriptionOwner owner, List<Subscription> found, List<SubscriptionOwner> missing, CancellationToken ct)
    {
        var cached = await backend.TryGetAsync(keys.Key(type, owner), ct);
        if (cached.IsError)
        {
            LogBackendFailure(nameof(backend.TryGetAsync), cached.Error);
        }

        if (cached.IsError || cached.Value is null)
        {
            missing.Add(owner);
        }
        else
        {
            found.AddRange(cached.Value);
        }
    }

    private async ValueTask<ErrorOr<IReadOnlyList<Subscription>>> ReadMissingOwnersAsync(
        MessageType type, List<SubscriptionOwner> missing, List<Subscription> found, CancellationToken ct)
    {
        CompanyId[] companies = [.. missing.Where(static owner => owner.Company is not null).Select(static owner => owner.Company!.Value)];
        UserId[] users = [.. missing.Where(static owner => owner.User is not null).Select(static owner => owner.User!.Value)];
        var stored = await inner.FindForDeliveryAsync(type, companies, users, ct);
        if (stored.IsError)
        {
            return stored.Error;
        }

        var byOwner = stored.Value.ToLookup(static subscription => subscription.Owner);
        foreach (var owner in missing)
        {
            Subscription[] ofOwner = [.. byOwner[owner]];
            found.AddRange(ofOwner);
            await SetAsync(type, owner, ofOwner, ct);
        }

        return found;
    }

    private async ValueTask SetAsync(MessageType type, SubscriptionOwner owner, Subscription[] subscriptions, CancellationToken ct)
    {
        var error = await backend.SetAsync(keys.Key(type, owner), subscriptions, [keys.TenantTag, keys.OwnerTag(owner)], expiration, ct);
        if (error is not null)
        {
            LogBackendFailure(nameof(backend.SetAsync), error);
        }
    }

    private async ValueTask RemoveOwnerAsync(SubscriptionOwner owner, CancellationToken ct)
    {
        var error = await backend.RemoveByTagAsync(keys.OwnerTag(owner), ct);
        if (error is not null)
        {
            LogBackendFailure(nameof(backend.RemoveByTagAsync), error);
        }
    }

    private void LogBackendFailure(string operation, Error error) =>
        Log.BackendFailed(logger, operation, key.Tenant?.Value, error.Type.Code, error.Type.Name, error.Message);
}
