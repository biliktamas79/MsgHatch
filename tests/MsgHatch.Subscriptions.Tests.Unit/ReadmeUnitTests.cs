using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task UseSubscriptions_ReadmeSetup_ResolvesTheSubscriptionQueries()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseSubscriptions()
            .UseRecipientDirectory<ShopRecipients>()
            .UseInMemorySubscriptionStore();
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var scope = provider.CreateAsyncScope();
        var subscriptions = await scope.ServiceProvider.GetRequiredService<ISubscriptionQueries>().GetSubscriptionsAsync(new SubscriptionQuery(), Ct);
        Assert.Empty(subscriptions.Value);
    }

    private sealed class ShopRecipients : IRecipientDirectory
    {
        public ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetUsersAsync(IReadOnlyList<UserId> users, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<IReadOnlyList<UserProfile>>.ForValue([]));

        public ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetCompanyUsersAsync(CompanyId company, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<IReadOnlyList<UserProfile>>.ForValue([]));

        public ValueTask<ErrorOr<CompanyProfile?>> GetCompanyAsync(CompanyId company, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<CompanyProfile?>.ForValue(null));
    }
}
