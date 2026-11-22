using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.AspNetCore.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(SubscriptionEndpointOptions));

    [Fact]
    public async Task MapMsgHatchSubscriptions_ReadmeSetup_MapsNoMeRoute()
    {
        // Arrange
        await using var app = await BuildAppAsync();

        // Act
        #region readme:setup
        app.MapMsgHatchSubscriptions()
            .RequireAuthorization("SubscriptionAdmin");
        #endregion

        // Assert
        var patterns = RoutePatterns(app);
        Assert.NotEmpty(patterns);
        Assert.DoesNotContain(patterns, IsMeRoute);
    }

    [Fact]
    public async Task MapMsgHatchSubscriptions_CurrentUserOfTheReadme_MapsTheMeRoutes()
    {
        // Arrange
        await using var app = await BuildAppAsync();

        // Act
        #region readme:setup-me
        app.MapMsgHatchSubscriptions("/subscriptions", o =>
        {
            o.CurrentUser = user => user.FindFirstValue("sub") is { } id ? UserId.Create(id).Value : null;
        });
        #endregion

        // Assert
        Assert.Contains(RoutePatterns(app), IsMeRoute);
    }

    private static Task<WebApplication> BuildAppAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.Services.AddMsgHatch().UseInMemoryStores().UseSubscriptions().UseInMemorySubscriptionStore();
        return Task.FromResult(builder.Build());
    }

    private static bool IsMeRoute(string pattern) =>
        pattern == "/subscriptions/me" || pattern.StartsWith("/subscriptions/me/", StringComparison.Ordinal);

    private static List<string> RoutePatterns(WebApplication app) =>
        [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Select(route => route.RoutePattern.RawText!)];
}
