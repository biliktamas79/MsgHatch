using Bunit;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Management;
using NSubstitute;
using static MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes.SubscriptionTestData;

namespace MsgHatch.Subscriptions.AdminUI.Tests.Unit;

public sealed class MessageTypesPageUnitTests : SubscriptionPageContext
{
    [Fact]
    public void Render_TwoStates_ShowsTheChannelsAndTheNumberOfAppliedSubscriptions()
    {
        Queries.GetMessageTypesAsync(new MessageTypeQuery(), Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<MessageTypeState>>(
            [new MessageTypeState(Requests, [Fax, Pager], [Subscription()]), new MessageTypeState(Notices, [], [])]));

        var cut = Render<MessageTypesPage>();

        var rows = cut.FindAll("tr[data-row]");
        Assert.Equal(["Requests", "Fax, Pager", "1"], rows[0].Children.Select(static cell => cell.TextContent));
        Assert.Equal(["Notices", "None", "0"], rows[1].Children.Select(static cell => cell.TextContent));
        Assert.Equal(
            ("Message types", 2, SubscriptionAdminRoutes.List.TrimStart('/'), true),
            (cut.Find("[data-list-header] h1").TextContent, cut.FindAll("[data-list-filters] input").Count,
                cut.Find("[data-list-header] a").GetAttribute("href"), cut.Find("[data-list-body]").QuerySelector("[data-list-scroll] table") is not null));
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        Queries.GetMessageTypesAsync(new MessageTypeQuery(), Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<MessageTypeState>>(Errors.Rejected));

        var cut = Render<MessageTypesPage>();

        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void ClickShow_CompanyAndUser_QueriesTheStatesOfTheScope()
    {
        var cut = Render<MessageTypesPage>();

        cut.Find("#msghatch-types-company").Change("42");
        cut.Find("#msghatch-types-user").Change(" 7 ");
        cut.Find("[data-action=filter]").Click();

        Queries.Received(1).GetMessageTypesAsync(new MessageTypeQuery(Company: new CompanyId("42"), User: new UserId("7")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickShow_UserIdThatIsNotValid_ShowsValidationFailedAndQueriesNothing()
    {
        var cut = Render<MessageTypesPage>();
        Queries.ClearReceivedCalls();

        cut.Find("#msghatch-types-user").Change(new string('u', 257));
        cut.Find("[data-action=filter]").Click();

        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Queries.DidNotReceiveWithAnyArgs().GetMessageTypesAsync(default!, Arg.Any<CancellationToken>());
    }
}
