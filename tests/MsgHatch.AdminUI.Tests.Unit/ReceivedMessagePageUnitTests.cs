using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class ReceivedMessagePageUnitTests : RegistryPageContext
{
    private static readonly Guid _id = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTime _created = new(2030, 1, 1, 10, 0, 5, DateTimeKind.Utc);

    [Fact]
    public void Render_Message_ReadsAPageOfItsOneIdWithTheTestMessages()
    {
        GivenMessage(Summary());

        RenderPage();

        DeliveryQueries.Received(1).GetReceivedMessagesAsync(Arg.Is<InboxQuery>(query => IsOneMessage(query)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Message_ShowsTheSummary()
    {
        GivenMessage(Summary());

        var cut = RenderPage();

        Assert.Equal(
            [
                ("id", "00000000-0000-0000-0000-000000000001"), ("status", "Pending"), ("type", "Requests"), ("tenant", "Tenant1"),
                ("tries", "1 / 5"), ("created", "2030-01-01 10:00:05 UTC"), ("last-try", "2030-01-01 10:01:05 UTC"),
                ("next-try", "2030-01-01 10:02:05 UTC"), ("lease", "2030-01-01 10:03:05 UTC"), ("expires", ""),
                ("last-error", "Timeout of the handler"), ("content-version", "3"), ("outgoing", "Show the outgoing messages of this event"),
            ],
            cut.FindAll("dd[data-field]").Select(field => (field.GetAttribute("data-field")!, SpacedTextOf(field))));
    }

    [Fact]
    public void Render_Message_LinksToTheOutboxListOfItsSourceMessage()
    {
        GivenMessage(Summary());

        var cut = RenderPage();

        Assert.Equal("msghatch/outbox?source=00000000-0000-0000-0000-000000000001", cut.Find("[data-action=outgoing]").GetAttribute("href"));
    }

    [Fact]
    public void Render_TestMessage_LinksToTheOutboxListWithTheTestMessages()
    {
        GivenMessage(Summary(MessageFlags.Test));

        var cut = RenderPage();

        Assert.Equal("msghatch/outbox?source=00000000-0000-0000-0000-000000000001&test=true", cut.Find("[data-action=outgoing]").GetAttribute("href"));
    }

    [Fact]
    public void ClickShowContent_Event_ReadsTheEventOnlyAfterTheClick()
    {
        GivenMessage(Summary());
        DeliveryQueries.GetReceivedContentAsync(_id, Arg.Any<CancellationToken>()).Returns(Ok(new MessageContent(_id, "{\"id\":1}")));
        var cut = RenderPage();
        var beforeTheClick = DeliveryQueries.ReceivedCalls().Count(static call => call.GetMethodInfo().Name == nameof(IDeliveryQueries.GetReceivedContentAsync));

        cut.Find("[data-action=show-content]").Click();

        Assert.Equal((0, "{\n  \"id\": 1\n}"), (beforeTheClick, cut.Find("[data-content]").TextContent));
    }

    [Fact]
    public void ClickShowContent_Rejected_ShowsTheTextOfRejectedAndKeepsTheSummary()
    {
        GivenMessage(Summary());
        DeliveryQueries.GetReceivedContentAsync(_id, Arg.Any<CancellationToken>()).Returns(Fail<MessageContent>(Errors.Rejected));
        var cut = RenderPage();

        cut.Find("[data-action=show-content]").Click();

        Assert.Equal((AdminErrors.RejectedText, "Requests"), (cut.Find(".alert-danger").TextContent, cut.Find("[data-field=type]").TextContent));
    }

    [Fact]
    public void ClickRetry_ChangedOne_RetriesTheQueryOfTheIdShowsTheCountsAndReadsTheMessageAgain()
    {
        GivenMessage(Summary());
        DeliveryCommands.RetryReceivedAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new DeliveryActionResult(1, 1)));
        var cut = RenderPage();

        cut.Find("[data-action=retry]").Click();

        DeliveryCommands.Received(1).RetryReceivedAsync(Arg.Is<InboxQuery>(query => IsOneMessage(query)), Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetReceivedMessagesAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>());
        Assert.Equal("Matched 1, changed 1.", cut.Find("[data-action-result].alert-success").TextContent);
    }

    [Fact]
    public void ClickCancel_ChangedZero_ShowsThatTheMessageDidNotChange()
    {
        GivenMessage(Summary());
        DeliveryCommands.CancelReceivedAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new DeliveryActionResult(1, 0)));
        var cut = RenderPage();

        cut.Find("[data-action=cancel]").Click();

        DeliveryCommands.Received(1).CancelReceivedAsync(Arg.Is<InboxQuery>(query => IsOneMessage(query)), Arg.Any<CancellationToken>());
        Assert.Equal("The message did not change, because it is done, cancelled, leased, or expired.", cut.Find("[data-action-result]").TextContent);
    }

    [Fact]
    public void Render_EmptyPage_ShowsNotFoundAndTheLinkBackWithNoAction()
    {
        var cut = RenderPage();

        Assert.Equal(
            (Errors.NotFound.Code.ToString(System.Globalization.CultureInfo.InvariantCulture), "msghatch/inbox", 0),
            (cut.Find(".alert-danger").GetAttribute("data-error-code"), cut.Find("[data-action=back]").GetAttribute("href"), cut.FindAll("[data-action=retry]").Count));
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        DeliveryQueries.GetReceivedMessagesAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<MessagePage<ReceivedMessageSummary>>(Errors.Unavailable));

        var cut = RenderPage();

        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    private static bool IsOneMessage(InboxQuery query) =>
        query.Ids is [var id] && id == _id && query with { Ids = null } == new InboxQuery(IncludeTest: true, Limit: 1);

    private static ReceivedMessageSummary Summary(MessageFlags flags = MessageFlags.None) =>
        new(
            new DeliveryState(
                _id, DeliveryStatus.Pending, 1, 5, _created, _created.AddMinutes(1), _created.AddMinutes(2), _created.AddMinutes(3), null,
                "Timeout of the handler"),
            new TenantId("Tenant1"), new MessageType("Requests"), 3, flags);

    private void GivenMessage(ReceivedMessageSummary summary) =>
        DeliveryQueries.GetReceivedMessagesAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new MessagePage<ReceivedMessageSummary>([summary], null)));

    private IRenderedComponent<ReceivedMessagePage> RenderPage() => Render<ReceivedMessagePage>(p => p.Add(x => x.Id, _id));
}
