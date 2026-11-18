using System.Net;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.AspNetCore.Tests.Unit;

/// <summary>
/// The HTTP endpoints of the subscription management API on a <c>TestServer</c>, with a manager substitute for each tenant
/// key (section 4.17, D54). This file has the reads, the tenant selection, and the error mapping.
/// </summary>
public sealed partial class SubscriptionEndpointsUnitTests
{
    private const string _anId = "0197f0c0-0000-7000-8000-000000000001";

    private const string _validDraft = """{"level":"Default","messageType":"B","kind":"OptOut","revision":1}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string, string?, string> RouteRows => new()
    {
        { "channels", "GET", "/subscriptions/channels", null, nameof(ISubscriptionQueries.GetChannelsAsync) },
        { "message types", "GET", "/subscriptions/message-types", null, nameof(ISubscriptionQueries.GetMessageTypesAsync) },
        { "list", "GET", "/subscriptions", null, nameof(ISubscriptionQueries.GetSubscriptionsAsync) },
        { "read", "GET", $"/subscriptions/{_anId}", null, nameof(ISubscriptionQueries.GetSubscriptionAsync) },
        { "create", "POST", "/subscriptions", _validDraft, nameof(ISubscriptionCommands.CreateAsync) },
        { "update", "PUT", $"/subscriptions/{_anId}", _validDraft, nameof(ISubscriptionCommands.UpdateAsync) },
        { "delete", "DELETE", $"/subscriptions/{_anId}", null, nameof(ISubscriptionCommands.DeleteAsync) },
    };

    public static TheoryData<string, string> InvalidQueryRows => new()
    {
        { "list: message type with '-'", "/subscriptions?messageType=B-1" },
        { "list: empty company id", "/subscriptions?companyId=" },
        { "list: user id with a control character", "/subscriptions?userId=%01" },
        { "list: unknown level", "/subscriptions?level=Owner" },
        { "list: level as a number", "/subscriptions?level=1" },
        { "list: level in lower case", "/subscriptions?level=user" },
        { "message types: message type of only whitespace", "/subscriptions/message-types?messageType=%20" },
        { "message types: company id with a control character", "/subscriptions/message-types?companyId=%01" },
        { "message types: empty user id", "/subscriptions/message-types?userId=" },
        { "channels: empty tenant id", "/subscriptions/channels?tenantId=" },
        { "read: tenant id with a control character", $"/subscriptions/{_anId}?tenantId=%01" },
    };

    public static TheoryData<string, ErrorType, int> ErrorRows => new()
    {
        { "ValidationFailed", Errors.ValidationFailed, 400 },
        { "NotFound", Errors.NotFound, 404 },
        { "Conflict", Errors.Conflict, 409 },
        { "Unavailable", Errors.Unavailable, 500 },
        { "SerializationFailed", Errors.SerializationFailed, 500 },
    };

    [Fact]
    public async Task GetChannels_TwoChannels_Gives200WithEachChannelAndItsFeaturesAsOneText()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Manager.GetChannelsAsync(Ct)
            .ReturnsForAnyArgs(EndpointHost.Success<IReadOnlyList<ChannelDescriptor>>([EmailChannel.Descriptor, SignalRChannel.Descriptor]));

        // Act
        var response = await host.GetAsync("/subscriptions/channels");

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK,
                """[{"id":"Email","name":"Email","features":"Languages, RichText","description":null,"enabled":true},{"id":"SignalR","name":"UI notification","features":"Languages, RawData, GroupDelivery","description":null,"enabled":true}]"""),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task GetMessageTypes_AllFilters_GivesTheFiltersToTheManagerAnd200WithTheStates()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        var query = new MessageTypeQuery(new MessageType("B"), Scenario.Acme, new UserId("1"));
        MessageTypeState[] states =
        [
            new(new MessageType("B"), [EmailChannel.Id], [Scenario.S4, Scenario.S6]),
            new(new MessageType("D"), [EmailChannel.Id, SignalRChannel.Id], []),
        ];
        host.Manager.GetMessageTypesAsync(query, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success<IReadOnlyList<MessageTypeState>>(states));

        // Act
        var response = await host.GetAsync("/subscriptions/message-types?messageType=B&companyId=100&userId=1");

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK,
                $$"""[{"messageType":"B","channels":["Email"],"appliedSubscriptions":[{{ExpectedJson.S4}},{{ExpectedJson.S6}}]},{"messageType":"D","channels":["Email","SignalR"],"appliedSubscriptions":[]}]"""),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task GetMessageTypes_NoFilter_GivesAQueryWithNoFilter()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Manager.GetMessageTypesAsync(new MessageTypeQuery(), Arg.Any<CancellationToken>())
            .Returns(EndpointHost.Success<IReadOnlyList<MessageTypeState>>([]));

        // Act
        var response = await host.GetAsync("/subscriptions/message-types");

        // Assert
        Assert.Equal((HttpStatusCode.OK, "[]"), (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task GetSubscriptions_AllFilters_GivesTheFiltersToTheManagerAnd200WithTheSubscriptions()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        var query = new SubscriptionQuery(new MessageType("B"), Scenario.Acme, new UserId("1"), SubscriptionLevel.User);
        host.Manager.GetSubscriptionsAsync(query, Arg.Any<CancellationToken>())
            .Returns(EndpointHost.Success<IReadOnlyList<Subscription>>([Scenario.S4, Scenario.S11]));

        // Act
        var response = await host.GetAsync("/subscriptions?messageType=B&companyId=100&userId=1&level=User");

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK, $"[{ExpectedJson.S4},{ExpectedJson.S11}]"),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task GetSubscriptions_NoFilter_GivesAQueryWithNoFilter()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Manager.GetSubscriptionsAsync(new SubscriptionQuery(), Arg.Any<CancellationToken>())
            .Returns(EndpointHost.Success<IReadOnlyList<Subscription>>([]));

        // Act
        var response = await host.GetAsync("/subscriptions");

        // Assert
        Assert.Equal((HttpStatusCode.OK, "[]"), (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task GetSubscription_StoredId_Gives200WithTheSubscription()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Manager.GetSubscriptionAsync(Scenario.S11.Id, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(Scenario.S11));

        // Act
        var response = await host.GetAsync($"/subscriptions/{Scenario.S11.Id}");

        // Assert
        Assert.Equal((HttpStatusCode.OK, ExpectedJson.S11), (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task GetSubscription_SettingsOfAnUnregisteredType_Throws()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        var subscription = TestSubscriptions.A(settings: new TestChannelSettings());
        host.Manager.GetSubscriptionAsync(subscription.Id, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(subscription));

        // Act
        var read = host.GetAsync($"/subscriptions/{subscription.Id}");

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => read);
    }

    [Theory]
    [MemberData(nameof(InvalidQueryRows))]
    public async Task Get_InvalidQueryValue_Gives400AndCallsNoManager(string row, string path)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        var problem = await EndpointHost.ProblemOf(await host.GetAsync(path));

        // Assert
        Assert.Equal(
            (row, (400, "ValidationFailed"), 0),
            (row, problem, host.Manager.ReceivedCalls().Count() + host.T2Manager.ReceivedCalls().Count()));
    }

    [Theory]
    [MemberData(nameof(RouteRows))]
    public async Task Route_TenantId_UsesOnlyTheManagerOfThatTenant(string row, string method, string path, string? body, string call)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        await host.SendAsync(new HttpMethod(method), path + "?tenantId=t2", body);

        // Assert
        Assert.Equal((row, call, 0), (row, CallsOf(host.T2Manager), host.Manager.ReceivedCalls().Count()));
    }

    [Theory]
    [MemberData(nameof(RouteRows))]
    public async Task Route_NoTenantId_UsesOnlyTheManagerOfNoTenant(string row, string method, string path, string? body, string call)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        await host.SendAsync(new HttpMethod(method), path, body);

        // Assert
        Assert.Equal((row, call, 0), (row, CallsOf(host.Manager), host.T2Manager.ReceivedCalls().Count()));
    }

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public async Task GetSubscription_ManagerError_GivesItsStatusWithAProblemWithTheErrorName(string row, ErrorType error, int status)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Manager.GetSubscriptionAsync(default, Ct).ReturnsForAnyArgs(EndpointHost.Failure<Subscription>(error));

        // Act
        var response = await host.GetAsync($"/subscriptions/{_anId}");

        // Assert
        Assert.Equal((status, row), await EndpointHost.ProblemOf(response));
    }

    private static string CallsOf(ISubscriptionManagement manager) =>
        string.Join(", ", manager.ReceivedCalls().Select(static call => call.GetMethodInfo().Name));
}
