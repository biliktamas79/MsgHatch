using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Caching;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.Caching;

/// <summary>The invalidation of the subscription cache of one tenant (section 4.15), with a substitute for the cache backend.</summary>
public sealed class SubscriptionCacheUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string?, string?, string> OwnerRows => new()
    {
        { "default", null, null, "msghatch:sub:t:t1:d" },
        { "company", "100", null, "msghatch:sub:t:t1:c:100" },
        { "user with a colon", null, "x:1", "msghatch:sub:t:t1:u:x%3A1" },
    };

    [Theory]
    [MemberData(nameof(OwnerRows))]
    public async Task InvalidateAsync_Owner_RemovesTheOwnerTagOfTheTenantOfItsKey(string row, string? company, string? user, string expectedTag)
    {
        // Arrange
        var backend = Substitute.For<ISubscriptionCacheBackend>();
        await using var provider = BuildWith(backend);

        // Act
        var error = await provider.GetRequiredKeyedService<ISubscriptionCache>(_t1).InvalidateAsync(OwnerOf(company, user), Ct);

        // Assert
        Assert.Equal((row, (Error?)null, expectedTag), (row, error, RemovedTags(backend)));
    }

    [Fact]
    public async Task InvalidateAllAsync_TwoKeys_RemovesTheTenantTagOfEachKeyOnly()
    {
        // Arrange
        var backend = Substitute.For<ISubscriptionCacheBackend>();
        await using var provider = BuildWith(backend);

        // Act
        await provider.GetRequiredKeyedService<ISubscriptionCache>(_t1).InvalidateAllAsync(Ct);
        await provider.GetRequiredService<ISubscriptionCache>().InvalidateAllAsync(Ct);

        // Assert
        Assert.Equal("msghatch:sub:t:t1, msghatch:sub:n", RemovedTags(backend));
    }

    [Fact]
    public async Task InvalidateAllAsync_BackendError_ReturnsTheSameError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var backend = ABackendThatFailsToRemove(error);
        await using var provider = BuildWith(backend);

        // Act
        var result = await provider.GetRequiredKeyedService<ISubscriptionCache>(_t1).InvalidateAllAsync(Ct);

        // Assert
        Assert.Same(error, result);
        await backend.Received(1).RemoveByTagAsync("msghatch:sub:t:t1", Ct);
    }

    private static SubscriptionOwner OwnerOf(string? company, string? user) =>
        company is not null ? SubscriptionOwner.ForCompany(new CompanyId(company))
        : user is not null ? SubscriptionOwner.ForUser(new UserId(user))
        : SubscriptionOwner.Default;

    private static ISubscriptionCacheBackend ABackendThatFailsToRemove(Error error)
    {
        var backend = Substitute.For<ISubscriptionCacheBackend>();
        backend.RemoveByTagAsync(default!, default).ReturnsForAnyArgs(error);
        return backend;
    }

    private static ServiceProvider BuildWith(ISubscriptionCacheBackend backend)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseSubscriptionCache(_ => backend);
        return services.Build();
    }

    private static string RemovedTags(ISubscriptionCacheBackend backend) =>
        string.Join(
            ", ",
            backend.ReceivedCalls()
                .Where(static call => call.GetMethodInfo().Name == nameof(ISubscriptionCacheBackend.RemoveByTagAsync))
                .Select(static call => (string)call.GetArguments()[0]!));
}
