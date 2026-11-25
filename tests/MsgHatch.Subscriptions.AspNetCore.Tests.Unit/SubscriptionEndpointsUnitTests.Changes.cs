using System.Net;
using MsgHatch.Channels.Email;
using MsgHatch.Channels.SignalR;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.AspNetCore.Tests.Unit;

/// <summary>The create, update, and delete routes: the draft of the body, the status codes, and the <c>Location</c> header.</summary>
public sealed partial class SubscriptionEndpointsUnitTests
{
    public static TheoryData<string, string, SubscriptionDraft> DraftRows => new()
    {
        {
            "M14: user email in Hungarian plain text",
            """{"level":"User","userId":"2","messageType":"C","kind":"Channel","channel":"Email","settings":{"language":"hu","format":"PlainText"}}""",
            new(SubscriptionOwner.ForUser(new UserId("2")), new MessageType("C"), SubscriptionKind.Channel, EmailChannel.Id,
                new EmailChannelSettings("hu", EmailFormat.PlainText))
        },
        {
            "SignalR settings",
            """{"level":"User","userId":"1","messageType":"C","kind":"Channel","channel":"SignalR","settings":{"language":"hu"}}""",
            new(SubscriptionOwner.ForUser(new UserId("1")), new MessageType("C"), SubscriptionKind.Channel, SignalRChannel.Id,
                new SignalRChannelSettings("hu"))
        },
        {
            "company subscription for named users, mandatory",
            """{"level":"Company","companyId":"100","messageType":"D","kind":"Channel","channel":"Email","settings":{},"recipients":{"kind":"NamedUsers","users":["2","5"]},"mandatory":true}""",
            new(SubscriptionOwner.ForCompany(Scenario.Acme), new MessageType("D"), SubscriptionKind.Channel, EmailChannel.Id,
                new EmailChannelSettings(), SubscriptionRecipients.ForUsers([new UserId("2"), new UserId("5")]), Mandatory: true)
        },
        {
            "company subscription for the company",
            """{"level":"Company","companyId":"100","messageType":"B","kind":"Channel","channel":"Email","settings":{"format":"Html"},"recipients":{"kind":"Company"}}""",
            new(SubscriptionOwner.ForCompany(Scenario.Acme), new MessageType("B"), SubscriptionKind.Channel, EmailChannel.Id,
                new EmailChannelSettings(), SubscriptionRecipients.Company)
        },
        {
            "default opt-out for all channels, with the scope users",
            """{"level":"Default","messageType":"B","kind":"OptOut","recipients":{"kind":"ScopeUsers","users":[]}}""",
            new(SubscriptionOwner.Default, new MessageType("B"), SubscriptionKind.OptOut, null, null, SubscriptionRecipients.ScopeUsers)
        },
        {
            "opt-out for one channel with the settings null",
            """{"level":"User","userId":"2","messageType":"E","kind":"OptOut","channel":"Email","settings":null}""",
            new(SubscriptionOwner.ForUser(new UserId("2")), new MessageType("E"), SubscriptionKind.OptOut, EmailChannel.Id, null)
        },
        {
            "SignalR settings on the channel Email with their settings type",
            """{"level":"User","userId":"1","messageType":"C","kind":"Channel","channel":"Email","settings":{"language":"hu"},"settingsType":"Settings:SignalR"}""",
            new(SubscriptionOwner.ForUser(new UserId("1")), new MessageType("C"), SubscriptionKind.Channel, EmailChannel.Id, new SignalRChannelSettings("hu"))
        },
        {
            "opt-out for all channels with the settings null",
            """{"level":"User","userId":"2","messageType":"E","kind":"OptOut","channel":null,"settings":null}""",
            new(SubscriptionOwner.ForUser(new UserId("2")), new MessageType("E"), SubscriptionKind.OptOut, null, null)
        },
    };

    public static TheoryData<string, string> InvalidBodyRows => new()
    {
        { "invalid JSON", "{" },
        { "the JSON null", "null" },
        { "an empty body", string.Empty },
        { "no message type", """{"level":"Default","kind":"OptOut"}""" },
        { "message type null", """{"level":"Default","messageType":null,"kind":"OptOut"}""" },
        { "message type with '-'", """{"level":"Default","messageType":"B-1","kind":"OptOut"}""" },
        { "no kind", """{"level":"Default","messageType":"B"}""" },
        { "unknown kind name", """{"level":"Default","messageType":"B","kind":"Push"}""" },
        { "kind as an undefined number", """{"level":"Default","messageType":"B","kind":7}""" },
        { "no level", """{"messageType":"B","kind":"OptOut"}""" },
        { "level as an undefined number", """{"level":5,"messageType":"B","kind":"OptOut"}""" },
        { "default level with a company", """{"level":"Default","companyId":"100","messageType":"B","kind":"OptOut"}""" },
        { "company level with no company", """{"level":"Company","messageType":"B","kind":"OptOut"}""" },
        { "company level with a user", """{"level":"Company","companyId":"100","userId":"1","messageType":"B","kind":"OptOut"}""" },
        { "user level with a company", """{"level":"User","companyId":"100","userId":"1","messageType":"B","kind":"OptOut"}""" },
        { "empty company id", """{"level":"Company","companyId":"","messageType":"B","kind":"OptOut"}""" },
        { "user id with a control character", """{"level":"User","userId":"\u0001","messageType":"B","kind":"OptOut"}""" },
        { "channel with '-'", """{"level":"Default","messageType":"B","kind":"OptOut","channel":"e-mail"}""" },
        { "unknown channel with settings", """{"level":"Default","messageType":"B","kind":"Channel","channel":"Fax","settings":{}}""" },
        { "settings with no channel", """{"level":"Default","messageType":"B","kind":"OptOut","settings":{"format":"Html"}}""" },
        { "unknown email format", """{"level":"Default","messageType":"B","kind":"Channel","channel":"Email","settings":{"format":"Bogus"}}""" },
        { "a settings type that is not a settings type", """{"level":"Default","messageType":"B","kind":"Channel","channel":"Email","settings":{},"settingsType":"Trace:Subscription"}""" },
        { "an unknown settings type", """{"level":"Default","messageType":"B","kind":"Channel","channel":"Email","settings":{},"settingsType":"Settings:Fax"}""" },
        { "settings as a number", """{"level":"Default","messageType":"B","kind":"Channel","channel":"Email","settings":5}""" },
        { "users for the company", """{"level":"Company","companyId":"100","messageType":"B","kind":"Channel","channel":"Email","settings":{},"recipients":{"kind":"Company","users":["2"]}}""" },
        { "users for the scope users", """{"level":"Default","messageType":"B","kind":"OptOut","recipients":{"kind":"ScopeUsers","users":["2"]}}""" },
        { "empty named user", """{"level":"Company","companyId":"100","messageType":"D","kind":"Channel","channel":"Email","settings":{},"recipients":{"kind":"NamedUsers","users":[""]}}""" },
        { "recipient kind as an undefined number", """{"level":"Default","messageType":"B","kind":"OptOut","recipients":{"kind":9}}""" },
    };

    [Theory]
    [MemberData(nameof(DraftRows))]
    public async Task Post_ValidBody_GivesTheDraftToTheManager(string row, string body, SubscriptionDraft draft)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Manager.CreateAsync(draft, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(Scenario.S6));

        // Act
        var response = await host.SendAsync(HttpMethod.Post, "/subscriptions", body);

        // Assert
        Assert.Equal((row, HttpStatusCode.Created), (row, response.StatusCode));
    }

    [Fact]
    public async Task Post_Created_Gives201WithTheSubscriptionAndItsLocation()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Manager.CreateAsync(default!, Ct).ReturnsForAnyArgs(EndpointHost.Success(Scenario.S6));

        // Act
        var response = await host.SendAsync(HttpMethod.Post, "/subscriptions", _validDraft);

        // Assert
        Assert.Equal(
            (HttpStatusCode.Created, $"/subscriptions/{Scenario.S6.Id}", ExpectedJson.S6),
            (response.StatusCode, response.Headers.Location?.OriginalString, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task Post_TenantId_GivesALocationWithTheTenantId()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.T2Manager.CreateAsync(default!, Ct).ReturnsForAnyArgs(EndpointHost.Success(Scenario.S6));

        // Act
        var response = await host.SendAsync(HttpMethod.Post, "/subscriptions/?tenantId=t2", _validDraft);

        // Assert
        Assert.Equal(
            (HttpStatusCode.Created, $"/subscriptions/{Scenario.S6.Id}?tenantId=t2"),
            (response.StatusCode, response.Headers.Location?.OriginalString));
    }

    [Theory]
    [MemberData(nameof(InvalidBodyRows))]
    public async Task Post_InvalidBody_Gives400AndCallsNoManager(string row, string body)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        var problem = await EndpointHost.ProblemOf(await host.SendAsync(HttpMethod.Post, "/subscriptions", body));

        // Assert
        Assert.Equal((row, (400, "ValidationFailed"), 0), (row, problem, host.Manager.ReceivedCalls().Count()));
    }

    [Theory]
    [InlineData("POST", "/subscriptions")]
    [InlineData("PUT", "/subscriptions/" + _anId)]
    public async Task Change_BodyAsPlainText_Gives415AndCallsNoManager(string method, string path)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        var response = await host.SendAsync(new HttpMethod(method), path, _validDraft, mediaType: "text/plain");

        // Assert
        Assert.Equal((HttpStatusCode.UnsupportedMediaType, 0), (response.StatusCode, host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task Put_Revision_GivesTheIdTheRevisionAndTheDraftToTheManagerAnd200()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        var draft = new SubscriptionDraft(
            SubscriptionOwner.ForUser(new UserId("1")), new MessageType("B"), SubscriptionKind.Channel, EmailChannel.Id,
            new EmailChannelSettings("en", EmailFormat.Html));
        host.Manager.UpdateAsync(Scenario.S6.Id, 3, draft, Arg.Any<CancellationToken>()).Returns(EndpointHost.Success(Scenario.S6));

        // Act
        var response = await host.SendAsync(
            HttpMethod.Put, $"/subscriptions/{Scenario.S6.Id}",
            """{"level":"User","userId":"1","messageType":"B","kind":"Channel","channel":"Email","settings":{"language":"en","format":"Html"},"revision":3}""");

        // Assert
        Assert.Equal((HttpStatusCode.OK, ExpectedJson.S6), (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task Put_OldRevision_Gives409()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Manager.UpdateAsync(default, default, default!, Ct).ReturnsForAnyArgs(EndpointHost.Failure<Subscription>(Errors.Conflict));

        // Act
        var response = await host.SendAsync(HttpMethod.Put, $"/subscriptions/{_anId}", _validDraft);

        // Assert
        Assert.Equal((409, "Conflict"), await EndpointHost.ProblemOf(response));
    }

    [Fact]
    public async Task Put_NoRevision_Gives400AndCallsNoManager()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        var problem = await EndpointHost.ProblemOf(
            await host.SendAsync(HttpMethod.Put, $"/subscriptions/{_anId}", """{"level":"Default","messageType":"B","kind":"OptOut"}"""));

        // Assert
        Assert.Equal(((400, "ValidationFailed"), 0), (problem, host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task Put_InvalidBody_Gives400AndCallsNoManager()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        var problem = await EndpointHost.ProblemOf(
            await host.SendAsync(HttpMethod.Put, $"/subscriptions/{_anId}", """{"level":"Default","messageType":"B","revision":1}"""));

        // Assert
        Assert.Equal(((400, "ValidationFailed"), 0), (problem, host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task Delete_Id_GivesTheIdToTheManagerAnd204()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        var response = await host.DeleteAsync($"/subscriptions/{_anId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await host.Manager.Received(1).DeleteAsync(Guid.Parse(_anId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ManagerError_GivesAProblem()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Manager.DeleteAsync(default, Ct).ReturnsForAnyArgs(new Error(Errors.Unavailable));

        // Act
        var response = await host.DeleteAsync($"/subscriptions/{_anId}");

        // Assert
        Assert.Equal((503, "Unavailable"), await EndpointHost.ProblemOf(response));
    }
}
