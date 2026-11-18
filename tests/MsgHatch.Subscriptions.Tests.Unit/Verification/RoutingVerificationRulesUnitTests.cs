using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.Tests.Unit.Verification;

/// <summary>The rules of a routing verification request that need no state (D248), with a row for each rule.</summary>
public sealed class RoutingVerificationRulesUnitTests
{
    public static TheoryData<string, RoutingVerificationRequest, int?> RequestRows => new()
    {
        { "a valid request", new RoutingVerificationRequest([new UserId("1")], ["en", "hu"]), null },
        { "no test users and no languages", new RoutingVerificationRequest([], []), null },
        { "an empty language", new RoutingVerificationRequest([new UserId("1")], ["en", ""]), 400 },
        { "a language of white space", new RoutingVerificationRequest([new UserId("1")], ["\t"]), 400 },
        { "a null language", new RoutingVerificationRequest([new UserId("1")], [null!]), 400 },
        { "no list of languages", new RoutingVerificationRequest([new UserId("1")], null!), 400 },
        { "no list of test users", new RoutingVerificationRequest(null!, ["en"]), 400 },
    };

    [Theory]
    [MemberData(nameof(RequestRows))]
    public void Check_Request_GivesTheCodeOfTheRule(string row, RoutingVerificationRequest request, int? expected)
    {
        // Act
        var error = RoutingVerificationRules.Check(request);

        // Assert
        Assert.Equal((row, expected), (row, error?.Type.Code));
    }

    [Fact]
    public void Check_NullRequest_ThrowsArgumentNullException()
    {
        // Act
        var exception = Record.Exception(static () => RoutingVerificationRules.Check(null!));

        // Assert
        Assert.Equal("request", Assert.IsType<ArgumentNullException>(exception).ParamName);
    }
}
