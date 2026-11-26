using Bunit;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using NSubstitute;
using static MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes.SubscriptionTestData;

namespace MsgHatch.Subscriptions.AdminUI.Tests.Unit;

public sealed class RoutingVerificationPageUnitTests : SubscriptionPageContext
{
    [Fact]
    public void ClickRun_UsersLanguagesAndMessageType_SendsTheRequestAndShowsTheReport()
    {
        // Arrange
        var passed = new RoutingCheck(Requests, "R1", SubscriptionOwner.Default, new DeliveryTarget(DeliveryTargetKind.User, "1"), Fax, "en",
            RoutingCheckOutcome.Passed, null, null, null);
        var failed = passed with { TestCase = "R2", Outcome = RoutingCheckOutcome.Failed, ErrorCode = 404, ErrorName = "NotFound" };
        Verifier.VerifyAsync(Arg.Any<RoutingVerificationRequest>(), Arg.Any<CancellationToken>()).Returns(Ok(new RoutingVerificationReport([passed, failed])));
        var cut = Render<RoutingVerificationPage>();
        cut.Find("#msghatch-verify-users").Change("1\n2");
        cut.Find("#msghatch-verify-languages").Change("en\nhu");
        cut.Find("#msghatch-verify-type").Change("Requests");

        // Act
        cut.Find("[data-action=verify]").Click();

        // Assert
        Assert.Equal("Passed: 1. Failed: 1. Skipped: 0.", cut.Find("[data-field=totals]").TextContent);
        var rows = cut.FindAll("tr[data-row]");
        Assert.Equal(["Passed", "Requests", "R1", "Default", "User 1", "Fax", "en", ""], rows[0].Children.Select(static cell => cell.TextContent));
        Assert.Equal("NotFound", rows[1].Children[7].TextContent);
        Verifier.Received(1).VerifyAsync(ARequestOf([new UserId("1"), new UserId("2")], ["en", "hu"], Requests), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickRun_EmptyReport_ShowsThatTheVerificationFoundNothingToCheck()
    {
        // Arrange
        Verifier.VerifyAsync(Arg.Any<RoutingVerificationRequest>(), Arg.Any<CancellationToken>()).Returns(Ok(new RoutingVerificationReport([])));
        var cut = Render<RoutingVerificationPage>();

        // Act
        cut.Find("[data-action=verify]").Click();

        // Assert
        Assert.Equal("The verification found nothing to check.", cut.Find("[data-state=empty]").TextContent);
        Verifier.Received(1).VerifyAsync(ARequestOf([], ["en"], type: null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickRun_Unavailable_ShowsTheTextOfUnavailable()
    {
        // Arrange
        Verifier.VerifyAsync(Arg.Any<RoutingVerificationRequest>(), Arg.Any<CancellationToken>()).Returns(Fail<RoutingVerificationReport>(Errors.Unavailable));
        var cut = Render<RoutingVerificationPage>();

        // Act
        cut.Find("[data-action=verify]").Click();

        // Assert
        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
        Assert.Empty(cut.FindAll("[data-field=totals]"));
        Verifier.Received(1).VerifyAsync(ARequestOf([], ["en"], type: null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickRun_UserIdThatIsNotValid_ShowsValidationFailedAndSendsNothing()
    {
        // Arrange
        var cut = Render<RoutingVerificationPage>();
        cut.Find("#msghatch-verify-users").Change(new string('u', 257));

        // Act
        cut.Find("[data-action=verify]").Click();

        // Assert
        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Verifier.DidNotReceiveWithAnyArgs().VerifyAsync(default!, Arg.Any<CancellationToken>());
    }

    private static RoutingVerificationRequest ARequestOf(UserId[] users, string[] languages, MessageType? type) =>
        Arg.Is<RoutingVerificationRequest>(request => request.TestUsers.SequenceEqual(users) && request.Languages.SequenceEqual(languages) && request.Type == type);
}
