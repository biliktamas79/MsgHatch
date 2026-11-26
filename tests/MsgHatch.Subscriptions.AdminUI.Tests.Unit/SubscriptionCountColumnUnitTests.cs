using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.AdminUI;
using MsgHatch.AdminUI.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes.SubscriptionTestData;

namespace MsgHatch.Subscriptions.AdminUI.Tests.Unit;

/// <summary>The column "Subscriptions" of the message types page of the core admin UI (D276).</summary>
public sealed class SubscriptionCountColumnUnitTests : SubscriptionPageContext
{
    private readonly IRegistryQueries _registry = Substitute.For<IRegistryQueries>();

    public SubscriptionCountColumnUnitTests()
    {
        _registry.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryMessageType>>(
        [
            new RegistryMessageType(Requests, null, null, [Fax], Enabled: true, [], Revision: 0),
            new RegistryMessageType(Notices, null, null, [Fax], Enabled: true, [], Revision: 0),
        ]));
        Services.AddKeyedSingleton(TenantKey.None, _registry);
        Services.AddMsgHatchAdminUI();
    }

    [Fact]
    public void Render_StoredSubscriptions_ShowsTheCountOfEachLevelAlsoForZero()
    {
        // Arrange
        GivenSubscriptions(
            Subscription(SubscriptionOwner.ForCompany(new CompanyId("42"))),
            Subscription(SubscriptionOwner.ForCompany(new CompanyId("43"))),
            Subscription(SubscriptionOwner.ForUser(new UserId("7"))));

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Equal(
            ["Default 0|Company 2|User 1", "Default 0|Company 0|User 0"],
            cut.FindAll("tr[data-row]").Select(static row => string.Join('|', row.QuerySelectorAll("td[data-column=Subscriptions] [data-item]").Select(static item => item.TextContent))));
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Counts_LinkEachCountToTheSubscriptionListWithItsMessageTypeAndLevel()
    {
        // Arrange
        GivenSubscriptions();

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Equal(
            [
                "msghatch/subscriptions?type=Requests&level=Default",
                "msghatch/subscriptions?type=Requests&level=Company",
                "msghatch/subscriptions?type=Requests&level=User",
            ],
            cut.FindAll("tr[data-row]")[0].QuerySelectorAll("td[data-column=Subscriptions] a[data-item]").Select(static link => link.GetAttribute("href")));
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_SubscriptionsUnavailable_ShowsTheErrorOfTheColumnAboveTheList()
    {
        // Arrange
        Queries.GetSubscriptionsAsync(Arg.Any<SubscriptionQuery>(), Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<Subscription>>(Errors.Unavailable));

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Equal(
            ($"Subscriptions: {AdminErrors.UnavailableText}", 2),
            (cut.Find("[data-column-error=Subscriptions]").TextContent, cut.FindAll("tr[data-row]").Count));
        Queries.Received(1).GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_RegisteredMessageTypesThatTheColumnCannotRead_ShowsTheErrorOfTheColumn()
    {
        // Arrange
        GivenSubscriptions();
        _registry.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(
            Ok<IReadOnlyList<RegistryMessageType>>([new RegistryMessageType(Requests, null, null, [Fax], Enabled: true, [], Revision: 0)]),
            Fail<IReadOnlyList<RegistryMessageType>>(Errors.Rejected));

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Equal($"Subscriptions: {AdminErrors.RejectedText}", cut.Find("[data-column-error=Subscriptions]").TextContent);
        _registry.Received(2).GetMessageTypesAsync(Arg.Any<CancellationToken>());
        Queries.DidNotReceiveWithAnyArgs().GetSubscriptionsAsync(default!, Arg.Any<CancellationToken>());
    }

    private void GivenSubscriptions(params Subscription[] subscriptions) =>
        Queries.GetSubscriptionsAsync(Arg.Any<SubscriptionQuery>(), Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<Subscription>>(subscriptions));
}
