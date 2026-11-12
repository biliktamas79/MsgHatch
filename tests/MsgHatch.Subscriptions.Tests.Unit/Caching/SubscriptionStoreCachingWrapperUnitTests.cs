using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Caching;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;
using NSubstitute.Core;

namespace MsgHatch.Subscriptions.Tests.Unit.Caching;

/// <summary>
/// The caching wrapper of the subscription store (section 4.15, D38), with substitutes for the store and the cache
/// backend, built with the setup methods.
/// </summary>
public sealed class SubscriptionStoreCachingWrapperUnitTests
{
    private const string _t1 = "msghatch:sub:t:t1";

    private static readonly TenantKey _keyT1 = TenantKey.For(new TenantId("t1"));
    private static readonly MessageType _a = new("A");
    private static readonly CompanyId _acmeId = new("100");
    private static readonly CompanyId _globexId = new("200");
    private static readonly UserId _user1Id = new("1");
    private static readonly UserId _user2Id = new("2");
    private static readonly Subscription _default = TestSubscriptions.A();
    private static readonly Subscription _acme = TestSubscriptions.A(owner: SubscriptionOwner.ForCompany(_acmeId));
    private static readonly Subscription _globex = TestSubscriptions.A(owner: SubscriptionOwner.ForCompany(_globexId));
    private static readonly Subscription _user1 = TestSubscriptions.A(owner: SubscriptionOwner.ForUser(_user1Id));
    private static readonly Subscription _user2 = TestSubscriptions.A(owner: SubscriptionOwner.ForUser(_user2Id));
    private static readonly Dictionary<Guid, string> _names = new()
    {
        [_default.Id] = "default", [_acme.Id] = "acme", [_globex.Id] = "globex", [_user1.Id] = "user1", [_user2.Id] = "user2",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Changes => ["Add", "Update", "Delete"];

    public static TheoryData<string> Reads => ["Query", "Get"];

    public static TheoryData<string, int> BackendFailureRows => new()
    {
        { nameof(ISubscriptionCacheBackend.TryGetAsync), 3 },
        { nameof(ISubscriptionCacheBackend.SetAsync), 3 },
        { nameof(ISubscriptionCacheBackend.RemoveByTagAsync), 1 },
    };

    public static TheoryData<string?, string, string> TenantRows => new()
    {
        { null, "msghatch:sub:n:A:c:100", "msghatch:sub:n, msghatch:sub:n:c:100" },
        { "t1", "msghatch:sub:t:t1:A:c:100", "msghatch:sub:t:t1, msghatch:sub:t:t1:c:100" },
        { "t2", "msghatch:sub:t:t2:A:c:100", "msghatch:sub:t:t2, msghatch:sub:t:t2:c:100" },
    };

    [Fact]
    public async Task FindForDeliveryAsync_MissForAllOwners_CallsTheStoreOneTimeWithAllCompaniesAndUsers()
    {
        // Arrange
        var store = AStore(_default, _acme, _user1);
        await using var provider = BuildWith(store, ABackend());

        // Act
        await StoreOf(provider).FindForDeliveryAsync(_a, [_acmeId, _globexId], [_user1Id, _user2Id], Ct);

        // Assert
        Assert.Equal([("100, 200", "1, 2")], FindCalls(store));
    }

    [Fact]
    public async Task FindForDeliveryAsync_MissForAllOwners_SetsTheSubscriptionsOfEachOwnerAlsoAnEmptyList()
    {
        // Arrange
        var backend = ABackend();
        await using var provider = BuildWith(AStore(_default, _acme, _user1), backend);

        // Act
        await StoreOf(provider).FindForDeliveryAsync(_a, [_acmeId, _globexId], [_user1Id, _user2Id], Ct);

        // Assert
        Assert.Equal(
            [($"{_t1}:A:d", "default"), ($"{_t1}:A:c:100", "acme"), ($"{_t1}:A:c:200", ""), ($"{_t1}:A:u:1", "user1"), ($"{_t1}:A:u:2", "")],
            SetCalls(backend).Select(static call => (call.Key, call.Names)));
    }

    [Fact]
    public async Task FindForDeliveryAsync_MissForAllOwners_ReturnsTheSubscriptionsOfTheStore()
    {
        // Arrange
        await using var provider = BuildWith(AStore(_default, _acme, _user1), ABackend());

        // Act
        var result = await StoreOf(provider).FindForDeliveryAsync(_a, [_acmeId, _globexId], [_user1Id, _user2Id], Ct);

        // Assert
        Assert.Equal("acme, default, user1", Names(result.Value));
    }

    [Fact]
    public async Task FindForDeliveryAsync_HitForSomeOwners_CallsTheStoreWithOnlyTheMissingOwners()
    {
        // Arrange
        var store = AStore(_default, _globex, _user2);
        await using var provider = BuildWith(store, ABackend(($"{_t1}:A:d", [_default]), ($"{_t1}:A:c:100", [_acme]), ($"{_t1}:A:u:1", [_user1])));

        // Act
        await StoreOf(provider).FindForDeliveryAsync(_a, [_acmeId, _globexId], [_user1Id, _user2Id], Ct);

        // Assert
        Assert.Equal([("200", "2")], FindCalls(store));
    }

    [Fact]
    public async Task FindForDeliveryAsync_HitForSomeOwners_ReturnsAllSubscriptionsWholeAndTheDefaultsOneTime()
    {
        // Arrange
        await using var provider = BuildWith(
            AStore(_default, _globex, _user2), ABackend(($"{_t1}:A:d", [_default]), ($"{_t1}:A:c:100", [_acme]), ($"{_t1}:A:u:1", [_user1])));

        // Act
        var result = await StoreOf(provider).FindForDeliveryAsync(_a, [_acmeId, _globexId], [_user1Id, _user2Id], Ct);

        // Assert
        Assert.Equal("acme, default, globex, user1, user2", Names(result.Value));
    }

    [Fact]
    public async Task FindForDeliveryAsync_HitForAllOwners_CallsNoStore()
    {
        // Arrange
        var store = AStore();
        await using var provider = BuildWith(store, ABackend(($"{_t1}:A:d", [_default]), ($"{_t1}:A:c:100", [_acme]), ($"{_t1}:A:u:1", [])));

        // Act
        var result = await StoreOf(provider).FindForDeliveryAsync(_a, [_acmeId], [_user1Id], Ct);

        // Assert
        Assert.Equal(("acme, default", 0), (Names(result.Value), store.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task FindForDeliveryAsync_DefaultsCachedAndOneUserMissing_SetsOnlyTheUser()
    {
        // Arrange
        var backend = ABackend(($"{_t1}:A:d", [_default]));
        await using var provider = BuildWith(AStore(_default, _user1), backend);

        // Act
        await StoreOf(provider).FindForDeliveryAsync(_a, [], [_user1Id], Ct);

        // Assert
        Assert.Equal([($"{_t1}:A:u:1", "user1")], SetCalls(backend).Select(static call => (call.Key, call.Names)));
    }

    [Theory]
    [MemberData(nameof(TenantRows))]
    public async Task FindForDeliveryAsync_TenantOfTheKey_GivesTheKeyAndTheTagsOfItsTenant(string? tenant, string expectedKey, string expectedTags)
    {
        // Arrange
        var backend = ABackend(("msghatch:sub:n:A:d", []), ($"{_t1}:A:d", []), ("msghatch:sub:t:t2:A:d", []));
        await using var provider = BuildWith(AStore(_acme), backend);

        // Act
        await provider.GetRequiredKeyedService<ISubscriptionStore>(TenantKey.For(tenant is null ? null : new TenantId(tenant)))
            .FindForDeliveryAsync(_a, [_acmeId], [], Ct);

        // Assert
        var call = Assert.Single(SetCalls(backend));
        Assert.Equal((expectedKey, expectedTags), (call.Key, string.Join(", ", call.Tags)));
    }

    [Fact]
    public async Task FindForDeliveryAsync_IdsWithColons_PercentEncodesThemInEachKeyAndTag()
    {
        // Arrange
        var key = TenantKey.For(new TenantId("a:X"));
        var backend = ABackend(("msghatch:sub:t:a%3AX:A:d", []));
        await using var provider = BuildWith(AStore(), backend);

        // Act
        await provider.GetRequiredKeyedService<ISubscriptionStore>(key).FindForDeliveryAsync(_a, [new CompanyId("u:5")], [new UserId("x:1%")], Ct);

        // Assert
        Assert.Equal(
            [
                ("msghatch:sub:t:a%3AX:A:c:u%3A5", "msghatch:sub:t:a%3AX, msghatch:sub:t:a%3AX:c:u%3A5"),
                ("msghatch:sub:t:a%3AX:A:u:x%3A1%25", "msghatch:sub:t:a%3AX, msghatch:sub:t:a%3AX:u:x%3A1%25"),
            ],
            SetCalls(backend).Select(static call => (call.Key, string.Join(", ", call.Tags))));
    }

    [Fact]
    public async Task FindForDeliveryAsync_OptionsInCode_SetsWithTheExpirationAndTheKeyPrefixOfTheOptions()
    {
        // Arrange
        var expiration = TimeSpan.FromMinutes(5);
        var backend = ABackend(("app:subs:t:t1:A:d", []));
        await using var provider = BuildWith(AStore(_user1), backend, o => { o.Expiration = expiration; o.KeyPrefix = "app:subs"; });

        // Act
        await StoreOf(provider).FindForDeliveryAsync(_a, [], [_user1Id], Ct);

        // Assert
        var call = Assert.Single(SetCalls(backend));
        Assert.Equal(
            ("app:subs:t:t1:A:u:1", "app:subs:t:t1, app:subs:t:t1:u:1", expiration),
            (call.Key, string.Join(", ", call.Tags), call.Expiration));
    }

    [Fact]
    public async Task FindForDeliveryAsync_InnerError_ReturnsTheSameErrorAndSetsNothing()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var backend = ABackend();
        await using var provider = BuildWith(AStoreThatFailsToFind(error), backend);

        // Act
        var result = await StoreOf(provider).FindForDeliveryAsync(_a, [_acmeId], [_user1Id], Ct);

        // Assert
        Assert.Equal((true, 0), (ReferenceEquals(error, result.Error), SetCalls(backend).Length));
    }

    [Fact]
    public async Task FindForDeliveryAsync_BackendTryGetError_CountsAsAMissAndReadsTheStore()
    {
        // Arrange
        var store = AStore(_default, _user1);
        await using var provider = BuildWith(store, ABackendThatFailsIn(nameof(ISubscriptionCacheBackend.TryGetAsync)));

        // Act
        var result = await StoreOf(provider).FindForDeliveryAsync(_a, [], [_user1Id], Ct);

        // Assert
        Assert.Equal(("default, user1", 1), (Names(result.Value), FindCalls(store).Length));
    }

    [Fact]
    public async Task FindForDeliveryAsync_BackendSetError_ReturnsTheSubscriptionsOfTheStore()
    {
        // Arrange
        await using var provider = BuildWith(AStore(_default, _user1), ABackendThatFailsIn(nameof(ISubscriptionCacheBackend.SetAsync)));

        // Act
        var result = await StoreOf(provider).FindForDeliveryAsync(_a, [], [_user1Id], Ct);

        // Assert
        Assert.Equal((false, "default, user1"), (result.IsError, Names(result.Value)));
    }

    [Theory]
    [MemberData(nameof(BackendFailureRows))]
    public async Task Operation_BackendError_LogsAWarningForEachFailedCallWithTheTenant(string operation, int expectedCount)
    {
        // Arrange
        await using var provider = BuildWith(AStore(_default, _user1), ABackendThatFailsIn(operation));
        var store = StoreOf(provider);

        // Act
        await store.FindForDeliveryAsync(_a, [_acmeId], [_user1Id], Ct);
        await store.AddAsync(_user1, Ct);

        // Assert
        Assert.Equal(
            Enumerable.Repeat((LogLevel.Warning, (string?)operation, (string?)"t1"), expectedCount),
            provider.LogRecords().Where(static record => record.Id.Id == 1135)
                .Select(static record => (record.Level, record.GetStructuredStateValue("Operation"), record.GetStructuredStateValue("Tenant"))));
    }

    [Fact]
    public async Task FindForDeliveryAsync_TwoHitsAndOneMiss_LogsTheCountsAtTheDebugLevel()
    {
        // Arrange
        await using var provider = BuildWith(AStore(_user1), ABackend(($"{_t1}:A:d", []), ($"{_t1}:A:c:100", [])));

        // Act
        await StoreOf(provider).FindForDeliveryAsync(_a, [_acmeId], [_user1Id], Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords(), static record => record.Id.Id == 1134);
        Assert.Equal(
            (LogLevel.Debug, "2", "1", "A", "t1"),
            (record.Level, record.GetStructuredStateValue("HitCount"), record.GetStructuredStateValue("MissCount"),
                record.GetStructuredStateValue("MessageType"), record.GetStructuredStateValue("Tenant")));
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task Change_InnerSuccess_RemovesTheOwnerTagOfTheSubscription(string operation)
    {
        // Arrange
        var backend = ABackend();
        await using var provider = BuildWith(AStore(_user1), backend);

        // Act
        var error = await ChangeAsync(StoreOf(provider), operation);

        // Assert
        Assert.Equal((operation, (Error?)null, $"{_t1}:u:1"), (operation, error, string.Join(", ", RemovedTags(backend))));
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task Change_InnerError_ReturnsTheSameErrorAndRemovesNothing(string operation)
    {
        // Arrange
        var error = new Error(Errors.Conflict);
        var backend = ABackend();
        await using var provider = BuildWith(AStoreThatFailsToChange(error), backend);

        // Act
        var result = await ChangeAsync(StoreOf(provider), operation);

        // Assert
        Assert.Equal((operation, true, 0), (operation, ReferenceEquals(error, result), RemovedTags(backend).Length));
    }

    [Fact]
    public async Task DeleteAsync_ReadError_ReturnsTheSameErrorAndDeletesNothing()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var store = AStoreThatFailsToRead(error);
        var backend = ABackend();
        await using var provider = BuildWith(store, backend);

        // Act
        var result = await StoreOf(provider).DeleteAsync(_user1.Id, Ct);

        // Assert
        Assert.Equal(
            (true, 0, 0),
            (ReferenceEquals(error, result), store.ReceivedCalls().Count(static call => call.GetMethodInfo().Name == nameof(ISubscriptionStore.DeleteAsync)),
                RemovedTags(backend).Length));
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_DeletesAndRemovesNoTag()
    {
        // Arrange
        var store = AStore();
        var backend = ABackend();
        await using var provider = BuildWith(store, backend);

        // Act
        var error = await StoreOf(provider).DeleteAsync(Guid.NewGuid(), Ct);

        // Assert
        Assert.Equal(
            ((Error?)null, 1, 0),
            (error, store.ReceivedCalls().Count(static call => call.GetMethodInfo().Name == nameof(ISubscriptionStore.DeleteAsync)),
                RemovedTags(backend).Length));
    }

    [Fact]
    public async Task AddAsync_BackendRemoveError_Succeeds()
    {
        // Arrange
        await using var provider = BuildWith(AStore(_user1), ABackendThatFailsIn(nameof(ISubscriptionCacheBackend.RemoveByTagAsync)));

        // Act
        var error = await StoreOf(provider).AddAsync(_user1, Ct);

        // Assert
        Assert.Null(error);
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task Read_AnyQuery_ReturnsTheResultOfTheStoreAndUsesNoCache(string operation)
    {
        // Arrange
        var backend = ABackend();
        await using var provider = BuildWith(AStore(_user1), backend);
        var store = StoreOf(provider);

        // Act
        var names = await ReadNamesAsync(store, operation);

        // Assert
        Assert.Equal((operation, "user1", 0), (operation, names, backend.ReceivedCalls().Count()));
    }

    private static ISubscriptionStore StoreOf(IServiceProvider provider) => provider.GetRequiredKeyedService<ISubscriptionStore>(_keyT1);

    private static ServiceProvider BuildWith(ISubscriptionStore store, ISubscriptionCacheBackend backend, Action<SubscriptionCacheOptions>? configure = null)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseSubscriptionStore((_, _) => store).UseSubscriptionCache(_ => backend, configure);
        return services.Build();
    }

    private static ISubscriptionStore AStore(params Subscription[] subscriptions)
    {
        var store = Substitute.For<ISubscriptionStore>();
        store.FindForDeliveryAsync(default, default!, default!, default).ReturnsForAnyArgs(ErrorOr<IReadOnlyList<Subscription>>.ForValue(subscriptions));
        store.QueryAsync(default!, default).ReturnsForAnyArgs(ErrorOr<IReadOnlyList<Subscription>>.ForValue(subscriptions));
        store.GetAsync(default, default).ReturnsForAnyArgs(ErrorOr<Subscription?>.ForValue(subscriptions.FirstOrDefault()));
        return store;
    }

    private static ISubscriptionStore AStoreThatFailsToFind(Error error)
    {
        var store = AStore();
        store.FindForDeliveryAsync(default, default!, default!, default).ReturnsForAnyArgs(error);
        return store;
    }

    private static ISubscriptionStore AStoreThatFailsToRead(Error error)
    {
        var store = AStore(_user1);
        store.GetAsync(default, default).ReturnsForAnyArgs(error);
        return store;
    }

    private static ISubscriptionStore AStoreThatFailsToChange(Error error)
    {
        var store = AStore(_user1);
        store.AddAsync(default!, default).ReturnsForAnyArgs(error);
        store.UpdateAsync(default!, default, default).ReturnsForAnyArgs(error);
        store.DeleteAsync(default, default).ReturnsForAnyArgs(error);
        return store;
    }

    private static ISubscriptionCacheBackend ABackend(params (string Key, Subscription[] Subscriptions)[] cached)
    {
        var entries = cached.ToDictionary(static entry => entry.Key, static entry => (IReadOnlyList<Subscription>)entry.Subscriptions);
        var backend = Substitute.For<ISubscriptionCacheBackend>();
        backend.TryGetAsync(default!, default)
            .ReturnsForAnyArgs(call => ErrorOr<IReadOnlyList<Subscription>?>.ForValue(entries.GetValueOrDefault(call.ArgAt<string>(0))));
        return backend;
    }

    private static ISubscriptionCacheBackend ABackendThatFailsIn(string operation)
    {
        var error = new Error(Errors.Unavailable);
        var backend = Substitute.For<ISubscriptionCacheBackend>();
        backend.TryGetAsync(default!, default)
            .ReturnsForAnyArgs(operation == nameof(ISubscriptionCacheBackend.TryGetAsync) ? error : ErrorOr<IReadOnlyList<Subscription>?>.ForValue(null));
        backend.SetAsync(default!, default!, default!, default, default).ReturnsForAnyArgs(operation == nameof(ISubscriptionCacheBackend.SetAsync) ? error : null);
        backend.RemoveByTagAsync(default!, default).ReturnsForAnyArgs(operation == nameof(ISubscriptionCacheBackend.RemoveByTagAsync) ? error : null);
        return backend;
    }

    private static async Task<string> ReadNamesAsync(ISubscriptionStore store, string operation) => operation switch
    {
        "Query" => Names((await store.QueryAsync(new SubscriptionQuery(), Ct)).Value),
        _ => Names([(await store.GetAsync(_user1.Id, Ct)).Value!]),
    };

    private static async Task<Error?> ChangeAsync(ISubscriptionStore store, string operation) => operation switch
    {
        "Add" => await store.AddAsync(_user1, Ct),
        "Update" => await store.UpdateAsync(_user1 with { Revision = 2 }, 1, Ct),
        _ => await store.DeleteAsync(_user1.Id, Ct),
    };

    private static string Names(IEnumerable<Subscription> subscriptions) =>
        string.Join(", ", subscriptions.Select(static subscription => _names[subscription.Id]).Order(StringComparer.Ordinal));

    private static (string Companies, string Users)[] FindCalls(ISubscriptionStore store) =>
    [
        .. CallsOf(store, nameof(ISubscriptionStore.FindForDeliveryAsync)).Select(static arguments => (
            string.Join(", ", ((IReadOnlyList<CompanyId>)arguments[1]!).Select(static company => company.Value)),
            string.Join(", ", ((IReadOnlyList<UserId>)arguments[2]!).Select(static user => user.Value)))),
    ];

    private static (string Key, string Names, string[] Tags, TimeSpan Expiration)[] SetCalls(ISubscriptionCacheBackend backend) =>
    [
        .. CallsOf(backend, nameof(ISubscriptionCacheBackend.SetAsync)).Select(static arguments => (
            (string)arguments[0]!, Names((IReadOnlyList<Subscription>)arguments[1]!), (string[])[.. (IReadOnlyList<string>)arguments[2]!],
            (TimeSpan)arguments[3]!)),
    ];

    private static string[] RemovedTags(ISubscriptionCacheBackend backend) =>
        [.. CallsOf(backend, nameof(ISubscriptionCacheBackend.RemoveByTagAsync)).Select(static arguments => (string)arguments[0]!)];

    private static IEnumerable<object?[]> CallsOf(object substitute, string method) =>
        substitute.ReceivedCalls().Where(call => call.GetMethodInfo().Name == method).Select(static call => call.GetArguments());
}
