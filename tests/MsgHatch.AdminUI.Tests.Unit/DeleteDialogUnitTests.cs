using Bunit;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class DeleteDialogUnitTests : BunitContext
{
    [Fact]
    public void Render_Closed_ShowsNoQuestion()
    {
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, false));

        Assert.Empty(cut.FindAll(".modal"));
    }

    [Fact]
    public void Render_Open_ShowsTheQuestion()
    {
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true).Add(x => x.Question, "Delete the subject?"));

        Assert.Equal("Delete the subject?", cut.Find(".modal-body p").TextContent);
    }

    [Fact]
    public void Render_Open_PutsCancelOnTheLeftAndDeleteOnTheRight()
    {
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true));

        Assert.Equal(
            ("Cancel", "Delete"),
            (cut.Find(".modal-footer [data-actions-start] [data-action=cancel-delete]").TextContent,
                cut.Find(".modal-footer [data-actions-end] [data-action=confirm-delete]").TextContent));
    }

    [Fact]
    public void ClickDelete_Open_RunsConfirmedOnly()
    {
        var confirmed = 0;
        var cancelled = 0;
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true)
            .Add(x => x.Confirmed, () => confirmed++).Add(x => x.Cancelled, () => cancelled++));

        cut.Find("[data-action=confirm-delete]").Click();

        Assert.Equal((1, 0), (confirmed, cancelled));
    }

    [Fact]
    public void ClickCancel_Open_RunsCancelledOnly()
    {
        var confirmed = 0;
        var cancelled = 0;
        var cut = Render<DeleteDialog>(p => p.Add(x => x.Open, true)
            .Add(x => x.Confirmed, () => confirmed++).Add(x => x.Cancelled, () => cancelled++));

        cut.Find("[data-action=cancel-delete]").Click();

        Assert.Equal((0, 1), (confirmed, cancelled));
    }
}
