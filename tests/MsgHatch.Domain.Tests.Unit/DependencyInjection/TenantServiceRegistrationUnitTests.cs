using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

/// <summary>The premises of the tenant services (D126) in the pinned version of Microsoft.Extensions.DependencyInjection.</summary>
public sealed class TenantServiceRegistrationUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));
    private static readonly TenantKey _t2 = TenantKey.For(new TenantId("t2"));

    [Fact]
    public async Task AnyKeySingleton_TwoResolutionsWithTheSameKey_GiveTheSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddKeyedSingleton<KeyHolder>(KeyedService.AnyKey, static (_, key) => new KeyHolder(key));
        await using var provider = services.Build();
        var first = provider.GetRequiredKeyedService<KeyHolder>(_t1);

        // Act
        var second = provider.GetRequiredKeyedService<KeyHolder>(_t1);

        // Assert
        Assert.Same(first, second);
    }

    [Fact]
    public async Task AnyKeySingleton_TwoKeys_GiveOneInstanceForEachKeyWithItsKey()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddKeyedSingleton<KeyHolder>(KeyedService.AnyKey, static (_, key) => new KeyHolder(key));
        await using var provider = services.Build();
        var first = provider.GetRequiredKeyedService<KeyHolder>(_t1);

        // Act
        var second = provider.GetRequiredKeyedService<KeyHolder>(_t2);

        // Assert
        Assert.NotSame(first, second);
        Assert.Equal([(object)_t1, _t2], [first.Key!, second.Key!]);
    }

    [Fact]
    public async Task TenantService_KeyThatIsNotATenantKey_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var provider = BuildWithOutboxStoreForEachKey();

        // Act
        var exception = Record.Exception(() => provider.GetKeyedService<IOutboxStore>("t1")); // a text, not a TenantKey

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task ScopedTenantService_TwoResolutionsWithTheSameKeyInOneScope_GiveTheSameInstance()
    {
        // Arrange
        await using var provider = BuildWithKeyCapturingRouter();
        await using var scope = provider.CreateAsyncScope();
        var first = scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<TestEvent>>(_t1);

        // Act
        var second = scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<TestEvent>>(_t1);

        // Assert
        Assert.Same(first, second);
    }

    [Fact]
    public async Task ScopedTenantService_TwoKeys_GiveTwoInstances()
    {
        // Arrange
        await using var provider = BuildWithKeyCapturingRouter();
        await using var scope = provider.CreateAsyncScope();
        var first = scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<TestEvent>>(_t1);

        // Act
        var second = scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<TestEvent>>(_t2);

        // Assert
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task TenantService_UnkeyedResolution_GivesTheInstanceOfTenantKeyNone()
    {
        // Arrange
        await using var provider = BuildWithKeyCapturingRouter();
        await using var scope = provider.CreateAsyncScope();
        var none = scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<TestEvent>>(TenantKey.None);

        // Act
        var unkeyed = scope.ServiceProvider.GetRequiredService<IMessageRouter<TestEvent>>();

        // Assert
        Assert.Same(none, unkeyed);
    }

    [Fact]
    public async Task TenantService_RequestedKey_IsTheTenantKeyConstructorArgument()
    {
        // Arrange
        await using var provider = BuildWithKeyCapturingRouter();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var router = (KeyCapturingRouter)scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<TestEvent>>(_t1);

        // Assert
        Assert.Equal(_t1, router.Key);
    }

    [Fact]
    public async Task TenantService_ParameterOfAnotherTenantService_GetsTheInstanceOfTheSameKey()
    {
        // Arrange
        await using var provider = BuildWithKeyCapturingRouter();
        await using var scope = provider.CreateAsyncScope();
        var composerOfT1 = scope.ServiceProvider.GetRequiredKeyedService<IEnvelopeComposer<TestEvent, TestEnvelope>>(_t1);

        // Act
        var router = (KeyCapturingRouter)scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<TestEvent>>(_t1);

        // Assert
        Assert.Same(composerOfT1, router.Composer);
    }

    [Fact]
    public async Task TenantService_ParameterOfAServiceThatIsNotATenantService_GetsTheUnkeyedInstance()
    {
        // Arrange
        await using var provider = BuildWithKeyCapturingRouter();
        await using var scope = provider.CreateAsyncScope();
        var time = provider.GetRequiredService<TimeProvider>();

        // Act
        var router = (KeyCapturingRouter)scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<TestEvent>>(_t1);

        // Assert
        Assert.Same(time, router.Time);
    }

    [Fact]
    public async Task SingletonTenantService_SameKeyInTwoScopes_GivesTheSameInstance()
    {
        // Arrange
        await using var provider = BuildWithOutboxStoreForEachKey();
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var ofFirstScope = first.ServiceProvider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        var ofSecondScope = second.ServiceProvider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Assert
        Assert.Same(ofFirstScope, ofSecondScope);
    }

    [Fact]
    public async Task SingletonTenantService_TwoKeys_GiveOneInstanceForEachKey()
    {
        // Arrange
        await using var provider = BuildWithOutboxStoreForEachKey();
        var ofT1 = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        var ofT2 = provider.GetRequiredKeyedService<IOutboxStore>(_t2);

        // Assert
        Assert.NotSame(ofT1, ofT2);
    }

    private static ServiceProvider BuildWithOutboxStoreForEachKey()
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false)
            .UseOutboxStore(static (_, _) => Substitute.For<IOutboxStore>());
        return services.Build();
    }

    private static ServiceProvider BuildWithKeyCapturingRouter()
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false)
            .AddTestChannel()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter<KeyCapturingRouter>()
                .AsChannel(
                    TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope,
                    static (_, _) => Substitute.For<IEnvelopeComposer<TestEvent, TestEnvelope>>()));
        return services.Build();
    }

    private sealed record KeyHolder(object? Key);
}
