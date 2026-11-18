using Bunit;
using Microsoft.AspNetCore.Components;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class AdminDialogUnitTests : BunitContext
{
    [Fact]
    public void Render_ActionsWithStartAndEnd_PutsStartOnTheLeftAndEndOnTheRight()
    {
        var cut = Render<AdminActions>(p => p.Add(x => x.Start, Markup("<a data-x>Back</a>")).Add(x => x.End, Markup("<button data-x>Save</button>")).Add(x => x.Class, "mt-3"));

        var bar = cut.Find("[data-actions]");
        Assert.Equal(
            (true, true, "Back", "Save", true),
            (bar.ClassList.Contains("justify-content-between"), bar.ClassList.Contains("mt-3"),
                cut.Find("[data-actions-start]").TextContent, cut.Find("[data-actions-end]").TextContent,
                cut.Find("[data-actions-end]").ClassList.Contains("ms-auto")));
    }

    [Fact]
    public void Render_ClosedDialog_ShowsNothing()
    {
        var cut = Render<AdminDialog>(p => p.Add(x => x.Open, false).Add(x => x.Title, "New"));

        Assert.Empty(cut.Nodes);
    }

    [Fact]
    public void Render_OpenDialog_ShowsTheTitleTheBodyAndTheActionsInTheFooter()
    {
        var cut = Render<AdminDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "New template")
            .AddChildContent("<p data-body>Fields</p>")
            .Add(x => x.Start, Markup("<button>Cancel</button>")).Add(x => x.End, Markup("<button>Open the editor</button>")));

        var title = cut.Find(".modal-title");
        Assert.Equal(
            ("New template", title.Id, "Fields", "Cancel", "Open the editor", false),
            (title.TextContent, cut.Find("[data-dialog]").GetAttribute("aria-labelledby"), cut.Find(".modal-body [data-body]").TextContent,
                cut.Find(".modal-footer [data-actions-start]").TextContent, cut.Find(".modal-footer [data-actions-end]").TextContent,
                cut.Find(".modal-dialog").ClassList.Contains("modal-lg")));
        Assert.Single(cut.FindAll(".modal-backdrop"));
    }

    [Fact]
    public void Render_OpenDialogWithNoActions_HasNoFooter()
    {
        var cut = Render<AdminDialog>(p => p.Add(x => x.Open, true).AddChildContent("<div data-actions>Own bar</div>"));

        Assert.Equal((0, "Own bar"), (cut.FindAll(".modal-footer").Count, cut.Find(".modal-body [data-actions]").TextContent));
    }

    [Fact]
    public void Render_OpenDialog_HasABodyThatIsAFlexColumn()
    {
        var cut = Render<AdminDialog>(p => p.Add(x => x.Open, true));

        Assert.Equal((true, true), (cut.Find(".modal-body").ClassList.Contains("d-flex"), cut.Find(".modal-body").ClassList.Contains("flex-column")));
    }

    [Fact]
    public void Render_WideDialog_HasTheLargeSize()
    {
        var cut = Render<AdminDialog>(p => p.Add(x => x.Open, true).Add(x => x.Wide, true));

        Assert.Contains("modal-lg", cut.Find(".modal-dialog").ClassList);
    }

    private static RenderFragment Markup(string html) => builder => builder.AddMarkupContent(0, html);
}
