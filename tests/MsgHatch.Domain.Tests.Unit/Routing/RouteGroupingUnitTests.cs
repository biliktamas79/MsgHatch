using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Routing;

public sealed class RouteGroupingUnitTests
{
    private static readonly ChannelId _email = new("Email");
    private static readonly ChannelId _signalR = new("SignalR");

    [Fact]
    public void ByChannel_RoutesOfTwoChannels_SplitsThemByChannelInTheOrderOfTheRouter()
    {
        // Arrange
        MessageRoute[] routes = [ARoute("u1", _email), ARoute("u2", _signalR), ARoute("u3", _email)];

        // Act
        var groups = RouteGrouping.ByChannel(routes);

        // Assert
        Assert.Equal(["Email: u1,u3 @ 0,2", "SignalR: u2 @ 1"], Describe(groups, static key => key.Name));
    }

    [Fact]
    public void ByLanguageAndScope_EmailRoutes_GroupsThemByLanguageAndTemplateScope()
    {
        // Arrange
        MessageRoute[] routes =
        [
            ARoute("u1", _email, language: "en"),
            ARoute("u2", _email, language: "hu"),
            ARoute("u3", _email, language: "en", templateScope: "acme"),
            ARoute("u4", _email, language: "en"),
        ];

        // Act
        var groups = RouteGrouping.ByLanguageAndScope(routes);

        // Assert
        Assert.Equal(
            ["en/: u1,u4 @ 0,3", "hu/: u2 @ 1", "en/acme: u3 @ 2"],
            Describe(groups, static key => $"{key.Language}/{key.TemplateScope}"));
    }

    [Fact]
    public void ByLanguageAndScope_LanguagesThatDifferOnlyInCase_AreOneGroup()
    {
        // Arrange
        MessageRoute[] routes = [ARoute("u1", _email, language: "hu-HU"), ARoute("u2", _email, language: "hu-hu")];

        // Act
        var groups = RouteGrouping.ByLanguageAndScope(routes);

        // Assert
        Assert.Equal(["hu-HU/: u1,u2 @ 0,1"], Describe(groups, static key => $"{key.Language}/{key.TemplateScope}"));
    }

    [Fact]
    public void ByChannel_EmptyRouteList_GivesNoGroups()
    {
        // Act
        var groups = RouteGrouping.ByChannel(ReadOnlyMemory<MessageRoute>.Empty);

        // Assert
        Assert.Empty(groups);
    }

    [Fact]
    public void ByLanguageAndScope_EmptyRouteList_GivesNoGroups()
    {
        // Act
        var groups = RouteGrouping.ByLanguageAndScope(ReadOnlyMemory<MessageRoute>.Empty);

        // Assert
        Assert.Empty(groups);
    }

    private static MessageRoute ARoute(
        string userId, ChannelId channel, string language = "en", string? templateScope = null) =>
        new(new DeliveryTarget(DeliveryTargetKind.User, userId), channel, new TestChannelSettings(), language,
            Address: null, templateScope, Trace: null);

    private static string[] Describe<TKey>(IReadOnlyList<RouteGroup<TKey>> groups, Func<TKey, string> describeKey) =>
        [.. groups.Select(group =>
            $"{describeKey(group.Key)}: {string.Join(',', group.Routes.ToArray().Select(static r => r.Target.Id))}" +
            $" @ {string.Join(',', group.Positions.ToArray())}")];
}
