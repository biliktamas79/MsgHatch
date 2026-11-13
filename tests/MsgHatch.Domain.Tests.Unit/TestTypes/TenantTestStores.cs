using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// An in-memory store for each tenant key through a test function, behind the dispatch composites of
/// <c>UseStorageForEachTenant</c> (D125), with no logging wrappers. The tenant source gives the tenants of the created
/// stores and of <see cref="Listed"/>, in the ordinal order of their ids. A dispatch store in
/// <see cref="OutboxDispatchStores"/> or <see cref="InboxDispatchStores"/> replaces the in-memory store of its key.
/// </summary>
public sealed class TenantTestStores
{
    private readonly Lock _lock = new();
    private readonly List<(TenantKey Key, InMemoryInboxStore Store)> _inbox = [];
    private readonly List<(TenantKey Key, InMemoryOutboxStore Store)> _outbox = [];

    public TenantTestStores(string? maxPollInterval = null)
    {
        var services = TestHost.CreateServices(configuration: maxPollInterval is null ? null : new Dictionary<string, string?>
        {
            ["MsgHatch:Dispatch:Inbox:MaxPollInterval"] = maxPollInterval,
            ["MsgHatch:Dispatch:Outbox:MaxPollInterval"] = maxPollInterval,
        });
        services.AddSingleton(this);
        services.AddMsgHatch(static o => o.EnableLogging = false)
            .UseStorageForEachTenant<TestTenantSource>(
                (_, key) => InboxOf(key).For(key),
                (_, key) => InboxDispatchStores.GetValueOrDefault(key) ?? InboxOf(key),
                (_, key) => OutboxOf(key).For(key),
                (_, key) => OutboxDispatchStores.GetValueOrDefault(key) ?? OutboxOf(key));
        Provider = services.Build();
    }

    public ServiceProvider Provider { get; }

    public HashSet<TenantId> Listed { get; } = [];

    /// <summary>An error that the tenant source gives in place of the tenants.</summary>
    public Error? TenantsError { get; set; }

    public Dictionary<TenantKey, IInboxDispatchStore> InboxDispatchStores { get; } = [];

    public Dictionary<TenantKey, IOutboxDispatchStore> OutboxDispatchStores { get; } = [];

    public TenantInboxDispatchStore InboxDispatch => (TenantInboxDispatchStore)Provider.GetRequiredService<IInboxDispatchStore>();

    public TenantOutboxDispatchStore OutboxDispatch => (TenantOutboxDispatchStore)Provider.GetRequiredService<IOutboxDispatchStore>();

    public IReadOnlyList<TenantId> Tenants
    {
        get
        {
            lock (_lock)
            {
                return
                [
                    .. _inbox.Select(static item => item.Key).Concat(_outbox.Select(static item => item.Key))
                        .Where(static key => key.Tenant is not null)
                        .Select(static key => key.Tenant!.Value)
                        .Concat(Listed)
                        .Distinct()
                        .OrderBy(static tenant => tenant.Value, StringComparer.Ordinal),
                ];
            }
        }
    }

    public IReadOnlyList<InMemoryInboxStore> InboxStores
    {
        get
        {
            lock (_lock)
            {
                return [.. _inbox.Select(static item => item.Store)];
            }
        }
    }

    public IReadOnlyList<InMemoryOutboxStore> OutboxStores
    {
        get
        {
            lock (_lock)
            {
                return [.. _outbox.Select(static item => item.Store)];
            }
        }
    }

    public IInboxStore InboxStore(TenantKey key) => Provider.GetRequiredKeyedService<IInboxStore>(key);

    public IOutboxStore OutboxStore(TenantKey key) => Provider.GetRequiredKeyedService<IOutboxStore>(key);

    public InMemoryInboxStore InboxOf(TenantKey key)
    {
        lock (_lock)
        {
            return _inbox.Find(item => item.Key == key).Store ?? Added(_inbox, key, new InMemoryInboxStore());
        }
    }

    public InMemoryOutboxStore OutboxOf(TenantKey key)
    {
        lock (_lock)
        {
            return _outbox.Find(item => item.Key == key).Store ?? Added(_outbox, key, new InMemoryOutboxStore());
        }
    }

    private static TStore Added<TStore>(List<(TenantKey Key, TStore Store)> stores, TenantKey key, TStore store)
    {
        stores.Add((key, store));
        return store;
    }
}

/// <summary>The tenant source of <see cref="TenantTestStores"/>.</summary>
public sealed class TestTenantSource(TenantTestStores stores) : ITenantSource
{
    public ValueTask<ErrorOr<IReadOnlyList<TenantId>>> GetTenantsAsync(CancellationToken ct) =>
        ValueTask.FromResult(stores.TenantsError is { } error ? error : ErrorOr<IReadOnlyList<TenantId>>.ForValue(stores.Tenants));
}
