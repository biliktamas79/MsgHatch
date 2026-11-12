using System.Net;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.Subscriptions.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.AspNetCore.Tests.Unit;

/// <summary>
/// The <c>/me</c> routes (D154): the user id of the signed-in user, the tenant of <c>CurrentTenant</c>, and only the
/// user-level subscriptions of that user. The scenario user 1 owns #6, and the user 2 owns #12.
/// </summary>
public sealed partial class SubscriptionEndpointsUnitTests
{
    public static TheoryData<string, string, string, string?> MeRouteRows => new()
    {
        { "channels", "GET", "/subscriptions/me/channels", null },
        { "message types", "GET", "/subscriptions/me/message-types", null },
        { "list", "GET", "/subscriptions/me", null },
        { "read", "GET", $"/subscriptions/me/{_anId}", null },
        { "create", "POST", "/subscriptions/me", _validDraft },
        { "update", "PUT", $"/subscriptions/me/{_anId}", _validDraft },
        { "delete", "DELETE", $"/subscriptions/me/{_anId}", null },
    };

    public static TheoryData<string, string, string?> OtherOwnerRows => new()
    {
        { "read", "GET", null },
        { "update", "PUT", _validDraft },
        { "delete", "DELETE", null },
    };

    [Fact]
    public async Task Me_NoCurrentUser_HasNoMeRoutes()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        var response = await host.GetAsync("/subscriptions/me", user: "1");

        // Assert
        Assert.Equal((HttpStatusCode.NotFound, 0), (response.StatusCode, host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task MapMsgHatchSubscriptions_CurrentTenantWithNoCurrentUser_Throws()
    {
        // Arrange
        static void TenantOnly(SubscriptionEndpointOptions options) => options.CurrentTenant = static _ => EndpointHost.T2;

        // Act
        var start = EndpointHost.StartAsync(TenantOnly);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(() => start);
    }

    [Theory]
    [MemberData(nameof(MeRouteRows))]
    public async Task MeRoute_TenantOfTheSignedInUser_UsesOnlyTheManagerOfThatTenant(
        string row, string method, string path, string? body)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);

        // Act
        await host.SendAsync(new HttpMethod(method), path + "?tenantId=other", body, user: "1", tenant: "t2");

        // Assert
        Assert.Equal((row, 1, 0), (row, host.T2Manager.ReceivedCalls().Count(), host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task MeRoute_NoCurrentTenant_UsesTheManagerOfNoTenant()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(static options => options.CurrentUser = static _ => new UserId("1"));

        // Act
        await host.GetAsync("/subscriptions/me/channels?tenantId=t2", tenant: "t2");

        // Assert
        Assert.Equal((nameof(ISubscriptionManager.GetChannelsAsync), 0), (CallsOf(host.Manager), host.T2Manager.ReceivedCalls().Count()));
    }

    [Theory]
    [MemberData(nameof(MeRouteRows))]
    public async Task MeRoute_SignedInUserWithNoUserId_Gives403AndCallsNoManager(
        string row, string method, string path, string? body)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);

        // Act
        var response = await host.SendAsync(new HttpMethod(method), path, body);

        // Assert
        Assert.Equal((row, HttpStatusCode.Forbidden, 0), (row, response.StatusCode, host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task GetMessageTypes_Me_GivesTheSignedInUserInPlaceOfTheOwnerFilters()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);
        var query = new MessageTypeQuery(new MessageType("B"), User: new UserId("1"));
        host.Manager.GetMessageTypesAsync(query, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success<IReadOnlyList<MessageTypeState>>([]));

        // Act
        var response = await host.GetAsync("/subscriptions/me/message-types?messageType=B&companyId=%01&userId=2", user: "1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetMessageTypes_MeInvalidMessageType_Gives400()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);

        // Act
        var response = await host.GetAsync("/subscriptions/me/message-types?messageType=B-1", user: "1");

        // Assert
        Assert.Equal((400, "ValidationFailed"), await EndpointHost.ProblemOf(response));
    }

    [Fact]
    public async Task GetSubscriptions_Me_GivesOnlyTheUserLevelOfTheSignedInUser()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);
        var query = new SubscriptionQuery(new MessageType("B"), User: new UserId("1"), Level: SubscriptionLevel.User);
        host.Manager.GetSubscriptionsAsync(query, Arg.Any<CancellationToken>())
            .Returns(EndpointHost.Success<IReadOnlyList<Subscription>>([Scenario.S6, Scenario.S6]));

        // Act
        var response = await host.GetAsync("/subscriptions/me?messageType=B&companyId=100&userId=2&level=Company", user: "1");

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK, $"[{ExpectedJson.S6},{ExpectedJson.S6}]"),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task GetSubscriptions_MeInvalidMessageType_Gives400()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);

        // Act
        var response = await host.GetAsync("/subscriptions/me?messageType=B-1", user: "1");

        // Assert
        Assert.Equal((400, "ValidationFailed"), await EndpointHost.ProblemOf(response));
    }

    [Fact]
    public async Task GetSubscription_MeOwnSubscription_Gives200()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);
        host.Manager.GetSubscriptionAsync(Scenario.S6.Id, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(Scenario.S6));

        // Act
        var response = await host.GetAsync($"/subscriptions/me/{Scenario.S6.Id}", user: "1");

        // Assert
        Assert.Equal((HttpStatusCode.OK, ExpectedJson.S6), (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Theory]
    [MemberData(nameof(OtherOwnerRows))]
    public async Task MeRoute_SubscriptionOfAnotherOwner_Gives404AndChangesNothing(string row, string method, string? body)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);
        host.Manager.GetSubscriptionAsync(Scenario.S12.Id, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(Scenario.S12));

        // Act
        var response = await host.SendAsync(new HttpMethod(method), $"/subscriptions/me/{Scenario.S12.Id}", body, user: "1");

        // Assert
        Assert.Equal((row, (404, "NotFound"), nameof(ISubscriptionManager.GetSubscriptionAsync)), (row, await EndpointHost.ProblemOf(response), CallsOf(host.Manager)));
    }

    [Theory]
    [MemberData(nameof(OtherOwnerRows))]
    public async Task MeRoute_UnknownIdAndAnotherOwner_GiveTheSameBody(string row, string method, string? body)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);
        host.Manager.GetSubscriptionAsync(Scenario.S12.Id, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(Scenario.S12));
        host.Manager.GetSubscriptionAsync(Guid.Parse(_anId), Arg.Any<CancellationToken>()).Returns(EndpointHost.Failure<Subscription>(Errors.NotFound));

        // Act
        var otherOwner = await host.SendAsync(new HttpMethod(method), $"/subscriptions/me/{Scenario.S12.Id}", body, user: "1");
        var unknown = await host.SendAsync(new HttpMethod(method), $"/subscriptions/me/{_anId}", body, user: "1");

        // Assert
        Assert.Equal(
            (row, HttpStatusCode.NotFound, await otherOwner.Content.ReadAsStringAsync(Ct)),
            (row, unknown.StatusCode, await unknown.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task GetSubscription_MeManagerErrorOtherThanNotFound_GivesThatError()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);
        host.Manager.GetSubscriptionAsync(default, Ct).ReturnsForAnyArgs(EndpointHost.Failure<Subscription>(Errors.Unavailable));

        // Act
        var response = await host.GetAsync($"/subscriptions/me/{_anId}", user: "1");

        // Assert
        Assert.Equal((500, "Unavailable"), await EndpointHost.ProblemOf(response));
    }

    [Fact]
    public async Task Post_Me_IgnoresTheOwnerFieldsAndGivesTheSignedInUserAsTheOwner()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);
        var draft = new SubscriptionDraft(SubscriptionOwner.ForUser(new UserId("1")), new MessageType("B"), SubscriptionKind.OptOut, null, null);
        host.Manager.CreateAsync(draft, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(Scenario.S6));

        // Act
        var response = await host.SendAsync(
            HttpMethod.Post, "/subscriptions/me", """{"level":"Company","companyId":"\u0001","messageType":"B","kind":"OptOut"}""", user: "1");

        // Assert
        Assert.Equal(
            (HttpStatusCode.Created, $"/subscriptions/me/{Scenario.S6.Id}"),
            (response.StatusCode, response.Headers.Location?.OriginalString));
    }

    [Fact]
    public async Task Put_MeOwnSubscription_GivesTheSignedInUserAsTheOwner()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);
        var draft = new SubscriptionDraft(
            SubscriptionOwner.ForUser(new UserId("1")), new MessageType("B"), SubscriptionKind.Channel, EmailChannel.Id, new EmailChannelSettings());
        host.Manager.GetSubscriptionAsync(Scenario.S6.Id, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(Scenario.S6));
        host.Manager.UpdateAsync(Scenario.S6.Id, 1, draft, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(Scenario.S6));

        // Act
        var response = await host.SendAsync(
            HttpMethod.Put, $"/subscriptions/me/{Scenario.S6.Id}",
            """{"messageType":"B","kind":"Channel","channel":"Email","settings":{},"revision":1}""", user: "1");

        // Assert
        Assert.Equal((HttpStatusCode.OK, ExpectedJson.S6), (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task Delete_MeOwnSubscription_Gives204AndDeletesIt()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);
        host.Manager.GetSubscriptionAsync(Scenario.S6.Id, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(Scenario.S6));

        // Act
        var response = await host.DeleteAsync($"/subscriptions/me/{Scenario.S6.Id}", user: "1");

        // Assert
        Assert.Equal(
            (HttpStatusCode.NoContent, $"{nameof(ISubscriptionManager.GetSubscriptionAsync)}, {nameof(ISubscriptionManager.DeleteAsync)}"),
            (response.StatusCode, CallsOf(host.Manager)));
    }

    [Fact]
    public async Task GetChannels_Me_Gives200WithTheChannels()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);
        host.Manager.GetChannelsAsync(Ct)
            .ReturnsForAnyArgs(EndpointHost.Success<IReadOnlyList<ChannelDescriptor>>([EmailChannel.Descriptor, EmailChannel.Descriptor]));

        // Act
        var response = await host.GetAsync("/subscriptions/me/channels", user: "1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
