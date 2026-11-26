using Bunit;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class DeleteDialogUnitTests : BunitContext
{
    [Fact]
    public void Render_Closed_ShowsNoQuestion()
    {
        // Act
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, false));

        // Assert
        Assert.Empty(cut.FindAll(".modal"));
    }

    [Fact]
    public void Render_Open_ShowsTheQuestion()
    {
        // Arrange
        const string question = "Delete the subject?";

        // Act
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true).Add(x => x.Question, question));

        // Assert
        Assert.Equal(question, cut.Find(".modal-body p").TextContent);
    }

    [Fact]
    public void Render_OpenWithDetails_ShowsTheDetailsAndThatTheDeleteCannotBeUndone()
    {
        // Act
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true).Add(x => x.Details, builder => builder.AddMarkupContent(0, "<dl><dt>Owner</dt><dd>Company 42</dd></dl>")));

        // Assert
        Assert.Equal(
            ("Company 42", "The delete cannot be undone."),
            (cut.Find(".modal-body [data-delete-details] dd").TextContent, cut.Find(".modal-body [data-delete-warning]").TextContent));
    }

    [Fact]
    public void Render_OpenWithNoDetails_ShowsNoDetailsArea()
    {
        // Act
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true));

        // Assert
        Assert.Equal((0, 1), (cut.FindAll("[data-delete-details]").Count, cut.FindAll("[data-delete-warning]").Count));
    }

    [Fact]
    public void Render_Open_PutsCancelOnTheLeftAndDeleteOnTheRight()
    {
        // Act
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true));

        // Assert
        Assert.Equal(
            ("Cancel", "Delete"),
            (cut.Find(".modal-footer [data-actions-start] [data-action=cancel-delete]").TextContent,
                cut.Find(".modal-footer [data-actions-end] [data-action=confirm-delete]").TextContent));
    }

    [Fact]
    public void Render_ActionReset_ShowsResetInTheTitleTheButtonAndTheWarning()
    {
        // Arrange
        const string action = "Reset";

        // Act
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true).Add(x => x.Action, action));

        // Assert
        Assert.Equal(
            (action, action, "The reset cannot be undone."),
            (cut.Find(".modal-title").TextContent, cut.Find("[data-action=confirm-delete]").TextContent, cut.Find("[data-delete-warning]").TextContent));
    }

    [Fact]
    public void Render_NoAction_ShowsDeleteInTheTitle()
    {
        // Act
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true));

        // Assert
        Assert.Equal("Delete", cut.Find(".modal-title").TextContent);
    }

    [Fact]
    public void ClickDelete_Open_RunsConfirmedOnly()
    {
        // Arrange
        var confirmed = 0;
        var cancelled = 0;
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true)
            .Add(x => x.Confirmed, () => confirmed++).Add(x => x.Cancelled, () => cancelled++));

        // Act
        cut.Find("[data-action=confirm-delete]").Click();

        // Assert
        Assert.Equal((1, 0), (confirmed, cancelled));
    }

    [Fact]
    public void ClickCancel_Open_RunsCancelledOnly()
    {
        // Arrange
        var confirmed = 0;
        var cancelled = 0;
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true)
            .Add(x => x.Confirmed, () => confirmed++).Add(x => x.Cancelled, () => cancelled++));

        // Act
        cut.Find("[data-action=cancel-delete]").Click();

        // Assert
        Assert.Equal((0, 1), (confirmed, cancelled));
    }
}
