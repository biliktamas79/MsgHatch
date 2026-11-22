using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Caching;
using MsgHatch.Subscriptions.Caching.Hybrid.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(SubscriptionCacheOptions));

    [Fact]
    public async Task UseHybridSubscriptionCache_SettingsOfTheReadme_ResolvesTheCache()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseSubscriptions()
            .UseInMemorySubscriptionStore()
            .UseHybridSubscriptionCache();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.NotNull(provider.GetRequiredService<ISubscriptionCache>());
        Assert.Equal(TimeSpan.FromMinutes(1), provider.GetRequiredService<IOptions<SubscriptionCacheOptions>>().Value.Expiration);
    }

    [Fact]
    public async Task UseHybridSubscriptionCache_ExpirationInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseSubscriptions()
            .UseInMemorySubscriptionStore()
            .UseHybridSubscriptionCache(o => o.Expiration = TimeSpan.FromMinutes(5));
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(5), provider.GetRequiredService<IOptions<SubscriptionCacheOptions>>().Value.Expiration);
    }
}
