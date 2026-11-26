using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class OutgoingMessagePageUnitTests : RegistryPageContext
{
    private static readonly Guid _id = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid _otherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid _source = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly DateTime _created = new(2030, 1, 1, 10, 0, 5, DateTimeKind.Utc);

    [Fact]
    public void Render_Message_ShowsTheSummary()
    {
        // Arrange
        GivenMessage(Detail());

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(
            [
                ("id", "00000000-0000-0000-0000-000000000001"), ("status", "Failed"), ("type", "Requests"), ("channel", "Email"),
                ("target", "User 1"), ("tenant", "Tenant1"), ("tries", "2 / 8"), ("created", "2030-01-01 10:00:05 UTC"),
                ("last-try", "2030-01-01 10:01:05 UTC"), ("next-try", "2030-01-01 10:02:05 UTC"), ("lease", ""), ("expires", "2030-01-02 10:00:05 UTC"),
                ("last-error", "Timeout: The provider did not answer."), ("content-type", "application/json, version 2"),
                ("source", "00000000-0000-0000-0000-0000000000aa"),
            ],
            cut.FindAll("dd[data-field]").Select(field => (field.GetAttribute("data-field")!, SpacedTextOf(field))));
    }

    [Fact]
    public void Render_Message_LinksTheSourceToThePageOfTheReceivedMessage()
    {
        // Arrange
        GivenMessage(Detail());

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal("msghatch/inbox/00000000-0000-0000-0000-0000000000aa", cut.Find("[data-action=source]").GetAttribute("href"));
    }

    [Fact]
    public void Render_TwoTries_ShowsARowForEachTryInTheOrderOfTheNumbers()
    {
        // Arrange
        GivenMessage(Detail());

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(
            [
                "1 2030-01-01 10:00:10 UTC 2030-01-01 10:00:11 UTC TransientError Timeout: The provider did not answer.",
                "2 2030-01-01 10:01:04 UTC 2030-01-01 10:01:05 UTC Succeeded provider-7",
            ],
            cut.FindAll("tr[data-try]").Select(SpacedTextOf));
    }

    [Fact]
    public void Render_NoTries_ShowsTheTextOfNoTries()
    {
        // Arrange
        GivenMessage(Detail() with { Tries = [] });

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal((1, 0), (cut.FindAll("[data-state=no-tries]").Count, cut.FindAll("tr[data-try]").Count));
    }

    [Fact]
    public void Render_Trace_ShowsTheTraceAsIndentedJson()
    {
        // Arrange
        GivenMessage(Detail());

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal("{\n  \"rule\": \"Requests\",\n  \"names\": [\n    \"Zoé\"\n  ]\n}", cut.Find("[data-trace]").TextContent);
    }

    [Fact]
    public void Render_NoTrace_ShowsTheTextOfNoTrace()
    {
        // Arrange
        GivenMessage(Detail() with { Trace = null });

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal((1, 0), (cut.FindAll("[data-state=no-trace]").Count, cut.FindAll("[data-trace]").Count));
    }

    [Fact]
    public void Render_Message_ReadsNoContent()
    {
        // Arrange
        GivenMessage(Detail());

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal((1, 0), (cut.FindAll("[data-action=show-content]").Count, cut.FindAll("[data-content]").Count));
        DeliveryQueries.DidNotReceiveWithAnyArgs().GetOutgoingContentAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickShowContent_Json_ShowsTheContentAsIndentedJson()
    {
        // Arrange
        GivenMessage(Detail());
        DeliveryQueries.GetOutgoingContentAsync(_id, Arg.Any<CancellationToken>()).Returns(Ok(new MessageContent(_id, "{\"to\":\"a@example.com\"}")));
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=show-content]").Click();

        // Assert
        Assert.Equal(("{\n  \"to\": \"a@example.com\"\n}", 0), (cut.Find("[data-content]").TextContent, cut.FindAll("[data-action=show-content]").Count));
    }

    [Fact]
    public void ClickShowContent_TextThatIsNotJson_ShowsTheText()
    {
        // Arrange
        const string content = "not { json";
        GivenMessage(Detail());
        DeliveryQueries.GetOutgoingContentAsync(_id, Arg.Any<CancellationToken>()).Returns(Ok(new MessageContent(_id, content)));
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=show-content]").Click();

        // Assert
        Assert.Equal(content, cut.Find("[data-content]").TextContent);
    }

    [Fact]
    public void ClickShowContent_Rejected_ShowsTheTextOfRejectedOnlyInTheContentArea()
    {
        // Arrange
        GivenMessage(Detail());
        DeliveryQueries.GetOutgoingContentAsync(_id, Arg.Any<CancellationToken>()).Returns(Fail<MessageContent>(Errors.Rejected));
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=show-content]").Click();

        // Assert
        Assert.Equal(
            (AdminErrors.RejectedText, 2, "Failed"),
            (cut.Find(".alert-danger").TextContent, cut.FindAll("tr[data-try]").Count, cut.Find("[data-field=status] [data-status]").TextContent));
    }

    [Fact]
    public void SetId_OtherMessageAfterShowContent_HidesTheContentOfTheFirstMessage()
    {
        // Arrange
        GivenMessage(Detail());
        DeliveryQueries.GetOutgoingMessageAsync(_otherId, Arg.Any<CancellationToken>()).Returns(Ok(Detail(_otherId)));
        DeliveryQueries.GetOutgoingContentAsync(_id, Arg.Any<CancellationToken>()).Returns(Ok(new MessageContent(_id, "{}")));
        var cut = RenderPage();
        cut.Find("[data-action=show-content]").Click();

        // Act
        cut.Render(p => p.Add(x => x.Id, _otherId));

        // Assert
        Assert.Equal((0, 1), (cut.FindAll("[data-content]").Count, cut.FindAll("[data-action=show-content]").Count));
        DeliveryQueries.Received(1).GetOutgoingMessageAsync(_otherId, Arg.Any<CancellationToken>());
        DeliveryQueries.DidNotReceive().GetOutgoingContentAsync(_otherId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickRetry_ChangedOne_RetriesTheQueryOfTheIdShowsTheCountsAndReadsTheMessageAgain()
    {
        // Arrange
        GivenMessage(Detail());
        DeliveryCommands.RetryOutgoingAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new DeliveryActionResult(1, 1)));
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=retry]").Click();

        // Assert
        Assert.Equal("Matched 1, changed 1.", cut.Find("[data-action-result].alert-success").TextContent);
        DeliveryCommands.Received(1).RetryOutgoingAsync(Arg.Is<OutboxQuery>(query => IsOneMessage(query)), Arg.Any<CancellationToken>());
        DeliveryCommands.DidNotReceiveWithAnyArgs().CancelOutgoingAsync(default!, Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetOutgoingMessageAsync(_id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickCancel_ChangedZero_ShowsThatTheMessageDidNotChange()
    {
        // Arrange
        GivenMessage(Detail());
        DeliveryCommands.CancelOutgoingAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok(new DeliveryActionResult(1, 0)));
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=cancel]").Click();

        // Assert
        Assert.Equal(
            "The message did not change, because it is done, cancelled, leased, or expired.",
            cut.Find("[data-action-result]").TextContent);
        DeliveryCommands.Received(1).CancelOutgoingAsync(Arg.Is<OutboxQuery>(query => IsOneMessage(query)), Arg.Any<CancellationToken>());
        DeliveryCommands.DidNotReceiveWithAnyArgs().RetryOutgoingAsync(default!, Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetOutgoingMessageAsync(_id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickCancel_Rejected_ShowsTheTextOfRejected()
    {
        // Arrange
        GivenMessage(Detail());
        DeliveryCommands.CancelOutgoingAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<DeliveryActionResult>(Errors.Rejected));
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=cancel]").Click();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find("[data-action-result].alert-danger").TextContent);
        DeliveryCommands.Received(1).CancelOutgoingAsync(Arg.Is<OutboxQuery>(query => IsOneMessage(query)), Arg.Any<CancellationToken>());
        DeliveryQueries.Received(2).GetOutgoingMessageAsync(_id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_NotFound_ShowsTheErrorAndTheLinkBackWithNoAction()
    {
        // Arrange
        DeliveryQueries.GetOutgoingMessageAsync(_id, Arg.Any<CancellationToken>()).Returns(Fail<OutgoingMessageDetail>(Errors.NotFound));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(
            (Errors.NotFound.Code.ToString(System.Globalization.CultureInfo.InvariantCulture), "msghatch/outbox", 0, 0),
            (cut.Find(".alert-danger").GetAttribute("data-error-code"), cut.Find("[data-action=back]").GetAttribute("href"),
                cut.FindAll("[data-action=retry]").Count, cut.FindAll("[data-action=cancel]").Count));
    }

    private static OutgoingMessageDetail Detail(Guid? id = null) =>
        new(
            new OutgoingMessageSummary(
                new DeliveryState(
                    id ?? _id, DeliveryStatus.Failed, 2, 8, _created, _created.AddMinutes(1), _created.AddMinutes(2), null, _created.AddDays(1),
                    "Timeout: The provider did not answer."),
                _source, new TenantId("Tenant1"), new MessageType("Requests"), Email, new DeliveryTarget(DeliveryTargetKind.User, "1"),
                "application/json", 2, MessageFlags.None),
            "{\"rule\":\"Requests\",\"names\":[\"Zoé\"]}",
            [
                new PublishTry(id ?? _id, 1, _created.AddSeconds(5), _created.AddSeconds(6), PublishOutcome.TransientError, null, 503, "Timeout", "The provider did not answer."),
                new PublishTry(id ?? _id, 2, _created.AddSeconds(59), _created.AddMinutes(1), PublishOutcome.Succeeded, "provider-7", null, null, null),
            ]);

    private static bool IsOneMessage(OutboxQuery query) => query.Ids is [var id] && id == _id && query with { Ids = null } == new OutboxQuery(IncludeTest: true);

    private void GivenMessage(OutgoingMessageDetail detail) =>
        DeliveryQueries.GetOutgoingMessageAsync(_id, Arg.Any<CancellationToken>()).Returns(Ok(detail));

    private IRenderedComponent<OutgoingMessagePage> RenderPage() => Render<OutgoingMessagePage>(p => p.Add(x => x.Id, _id));
}
