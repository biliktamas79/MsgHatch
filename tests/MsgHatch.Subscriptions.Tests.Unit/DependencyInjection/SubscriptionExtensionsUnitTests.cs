using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.DependencyInjection;

public sealed class SubscriptionExtensionsUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));
    private static readonly TenantKey _t2 = TenantKey.For(new TenantId("t2"));

    [Fact]
    public async Task UseSubscriptionStore_TwoKeys_GivesOneSingletonForEachKeyFromItsFactory()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = false).UseSubscriptionStore(static (_, key) => new KeyedStore(key));
        await using var provider = services.Build();

        // Act
        object[] stores =
        [
            provider.GetRequiredKeyedService<ISubscriptionStore>(_t1), provider.GetRequiredKeyedService<ISubscriptionStore>(_t1),
            provider.GetRequiredKeyedService<ISubscriptionStore>(_t2), provider.GetRequiredService<ISubscriptionStore>(),
        ];

        // Assert
        Assert.Equal(
            [(_t1, true), (_t1, true), (_t2, false), (TenantKey.None, false)],
            stores.Select(store => (((KeyedStore)store).Key, ReferenceEquals(store, stores[0]))));
    }

    [Fact]
    public async Task UseInMemorySubscriptionStore_TwoKeys_GivesOneInMemoryStoreForEachKey()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = false).UseInMemorySubscriptionStore();
        await using var provider = services.Build();

        // Act
        ISubscriptionStore[] stores =
        [
            provider.GetRequiredKeyedService<ISubscriptionStore>(_t1), provider.GetRequiredKeyedService<ISubscriptionStore>(_t1),
            provider.GetRequiredKeyedService<ISubscriptionStore>(_t2),
        ];

        // Assert
        Assert.Equal([true, true, false], stores.Select(store => store is InMemorySubscriptionStore && ReferenceEquals(store, stores[0])));
    }

    [Fact]
    public async Task UseInMemorySubscriptionStore_AfterUseSubscriptionStore_NamesBothMethods()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseInMemoryStores()
            .UseSubscriptionStore((_, _) => Substitute.For<ISubscriptionStore>())
            .UseInMemorySubscriptionStore();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            ["UseSubscriptionStore and UseInMemorySubscriptionStore select an implementation of ISubscriptionStore."],
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    [Fact]
    public async Task UseSubscriptionStore_SecondSelection_FailsTheStartupValidationAndNamesBothMethods()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseInMemoryStores()
            .UseSubscriptionStore((_, _) => Substitute.For<ISubscriptionStore>(), "UseFirstSubscriptionStore")
            .UseSubscriptionStore((_, _) => Substitute.For<ISubscriptionStore>());
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            ["UseFirstSubscriptionStore and UseSubscriptionStore select an implementation of ISubscriptionStore."],
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    [Fact]
    public async Task UseRecipientDirectory_SecondSelection_FailsTheStartupValidationAndNamesBothMethods()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseInMemoryStores()
            .UseRecipientDirectory((_, _) => Substitute.For<IRecipientDirectory>())
            .UseRecipientDirectory((_, _) => Substitute.For<IRecipientDirectory>());
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            ["UseRecipientDirectory and UseRecipientDirectory select an implementation of IRecipientDirectory."],
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    [Fact]
    public async Task UseRecipientDirectory_TwoScopes_GivesOneInstanceForEachKeyInEachScope()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = false).UseRecipientDirectory<KeyedDirectory>();
        await using var provider = services.Build();
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        // Act
        object[] directories =
        [
            first.ServiceProvider.GetRequiredKeyedService<IRecipientDirectory>(_t1),
            first.ServiceProvider.GetRequiredKeyedService<IRecipientDirectory>(_t1),
            second.ServiceProvider.GetRequiredKeyedService<IRecipientDirectory>(_t1),
        ];

        // Assert
        Assert.Equal(
            [(_t1, true), (_t1, true), (_t1, false)],
            directories.Select(directory => (((KeyedDirectory)directory).Key, ReferenceEquals(directory, directories[0]))));
    }

    private sealed class KeyedDirectory(TenantKey key) : IRecipientDirectory
    {
        public TenantKey Key { get; } = key;

        public ValueTask<Domain.Failures.ErrorOr<IReadOnlyList<UserProfile>>> GetUsersAsync(IReadOnlyList<UserId> users, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<Domain.Failures.ErrorOr<IReadOnlyList<UserProfile>>> GetCompanyUsersAsync(CompanyId company, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<Domain.Failures.ErrorOr<CompanyProfile?>> GetCompanyAsync(CompanyId company, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class KeyedStore(TenantKey key) : ISubscriptionStore
    {
        public TenantKey Key { get; } = key;

        public ValueTask<Domain.Failures.ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
            MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<Domain.Failures.ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<Domain.Failures.ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();

        public ValueTask<Domain.Failures.Error?> AddAsync(Subscription subscription, CancellationToken ct) => throw new NotSupportedException();

        public ValueTask<Domain.Failures.Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<Domain.Failures.Error?> DeleteAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
}
