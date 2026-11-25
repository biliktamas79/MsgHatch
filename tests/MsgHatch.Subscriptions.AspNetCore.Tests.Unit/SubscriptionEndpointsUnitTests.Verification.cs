using System.Net;
using MsgHatch.Channels.Email;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using NSubstitute;

namespace MsgHatch.Subscriptions.AspNetCore.Tests.Unit;

/// <summary>The endpoint <c>POST {prefix}/verification</c> of the routing verification (section 4.17, D163).</summary>
public sealed partial class SubscriptionEndpointsUnitTests
{
    private const string _verificationBody = """{"testUsers":["1","3"],"languages":["en","hu"],"messageType":"B"}""";

    public static TheoryData<string, string?, string> VerificationTenantRows => new()
    {
        { "tenantId t2", "?tenantId=t2", "t2" },
        { "no tenantId", null, "none" },
    };

    public static TheoryData<string, string, string> InvalidVerificationRows => new()
    {
        { "empty user id", "/subscriptions/verification", """{"testUsers":[""],"languages":["en"]}""" },
        { "message type with '-'", "/subscriptions/verification", """{"testUsers":["1"],"languages":["en"],"messageType":"B-1"}""" },
        { "no languages", "/subscriptions/verification", """{"testUsers":["1"]}""" },
        { "null test users", "/subscriptions/verification", """{"testUsers":null,"languages":["en"]}""" },
        { "empty language", "/subscriptions/verification", """{"testUsers":["1"],"languages":[" "]}""" },
        { "JSON null", "/subscriptions/verification", "null" },
        { "empty tenant id", "/subscriptions/verification?tenantId=", _verificationBody },
    };

    [Fact]
    public async Task Verify_Body_GivesTheTestUsersTheLanguagesAndTheMessageTypeToTheVerifier()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Verifier.VerifyAsync(default!, Ct).ReturnsForAnyArgs(EndpointHost.Success(new RoutingVerificationReport([])));

        // Act
        var response = await host.SendAsync(HttpMethod.Post, "/subscriptions/verification", _verificationBody);

        // Assert
        var request = (RoutingVerificationRequest)host.Verifier.ReceivedCalls().Single().GetArguments()[0]!;
        Assert.Equal(
            (HttpStatusCode.OK, "1,3", "en,hu", "B"),
            (response.StatusCode, string.Join(',', request.TestUsers.Select(static user => user.Value)), string.Join(',', request.Languages),
                request.Type?.Name));
    }

    [Fact]
    public async Task Verify_Report_Gives200WithEachRowItsTraceAndTheCounts()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        var userOne = SubscriptionOwner.ForUser(new UserId("1"));
        var trace = new ResolutionTrace(
            new SubscriptionSource(Scenario.S6.Id, userOne, RecipientKind.ScopeUsers, Mandatory: false), TargetSource.ScopeUser,
            LanguageSource.ChannelSettings, AddressSource.UserProfile, []);
        var type = new MessageType("B");
        RoutingCheck[] checks =
        [
            new(type, "basic", userOne, new DeliveryTarget(DeliveryTargetKind.User, "1"), EmailChannel.Id, "en", RoutingCheckOutcome.Passed, null, null, trace),
            new(type, "basic", null, null, null, "hu", RoutingCheckOutcome.Failed, 404, "NotFound", null),
            new(type, null, SubscriptionOwner.ForCompany(Scenario.Acme), null, EmailChannel.Id, null, RoutingCheckOutcome.Skipped, null, null, null),
        ];
        host.Verifier.VerifyAsync(default!, Ct).ReturnsForAnyArgs(EndpointHost.Success(new RoutingVerificationReport(checks)));

        // Act
        var response = await host.SendAsync(HttpMethod.Post, "/subscriptions/verification", _verificationBody);

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK,
                """{"checks":[{"messageType":"B","testCase":"basic","level":"User","companyId":null,"userId":"1","targetKind":"User","targetId":"1","channel":"Email","language":"en","outcome":"Passed","errorCode":null,"errorName":null,"trace":{"applied":{"id":"""
                + $"\"{Scenario.S6.Id}\""
                + ""","owner":{"level":"User","company":null,"user":"1"},"recipients":"ScopeUsers","mandatory":false},"target":"ScopeUser","language":"ChannelSettings","address":"UserProfile","skipped":[]}},"""
                + """{"messageType":"B","testCase":"basic","level":null,"companyId":null,"userId":null,"targetKind":null,"targetId":null,"channel":null,"language":"hu","outcome":"Failed","errorCode":404,"errorName":"NotFound","trace":null},"""
                + """{"messageType":"B","testCase":null,"level":"Company","companyId":"100","userId":null,"targetKind":null,"targetId":null,"channel":"Email","language":null,"outcome":"Skipped","errorCode":null,"errorName":null,"trace":null}],"""
                + "\"passed\":1,\"failed\":1,\"skipped\":1}"),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Theory]
    [MemberData(nameof(VerificationTenantRows))]
    public async Task Verify_TenantId_UsesOnlyTheVerifierOfThatTenantKey(string row, string? query, string usedVerifier)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        await host.SendAsync(HttpMethod.Post, "/subscriptions/verification" + query, _verificationBody);

        // Assert
        Assert.Equal(
            (row, usedVerifier == "t2" ? (0, 1) : (1, 0)),
            (row, (host.Verifier.ReceivedCalls().Count(), host.T2Verifier.ReceivedCalls().Count())));
    }

    [Theory]
    [MemberData(nameof(InvalidVerificationRows))]
    public async Task Verify_InvalidBodyOrQuery_Gives400AndCallsNoVerifier(string row, string path, string body)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        var response = await host.SendAsync(HttpMethod.Post, path, body);

        // Assert
        Assert.Equal((row, (400, "ValidationFailed"), 0), (row, await EndpointHost.ProblemOf(response), host.Verifier.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task Verify_BodyAsPlainText_Gives415AndCallsNoVerifier()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();

        // Act
        var response = await host.SendAsync(HttpMethod.Post, "/subscriptions/verification", _verificationBody, mediaType: "text/plain");

        // Assert
        Assert.Equal((HttpStatusCode.UnsupportedMediaType, 0), (response.StatusCode, host.Verifier.ReceivedCalls().Count()));
    }

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public async Task Verify_VerifierError_GivesItsStatusWithAProblemWithTheErrorName(string row, ErrorType error, int status)
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync();
        host.Verifier.VerifyAsync(default!, Ct).ReturnsForAnyArgs(EndpointHost.Failure<RoutingVerificationReport>(error));

        // Act
        var response = await host.SendAsync(HttpMethod.Post, "/subscriptions/verification", _verificationBody);

        // Assert
        Assert.Equal((status, row), await EndpointHost.ProblemOf(response));
    }

    [Fact]
    public async Task Verify_MeRoute_IsNotMapped_D163()
    {
        // Arrange
        await using var host = await EndpointHost.StartAsync(EndpointHost.WithSignedInUser);

        // Act
        var response = await host.SendAsync(HttpMethod.Post, "/subscriptions/me/verification", _verificationBody, user: "1");

        // Assert
        Assert.Equal((false, 0), (response.IsSuccessStatusCode, host.Verifier.ReceivedCalls().Count()));
    }
}
