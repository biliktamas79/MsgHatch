using Bunit;
using Microsoft.AspNetCore.Components;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class AdminDialogUnitTests : BunitContext
{
    [Fact]
    public void Render_ActionsWithStartAndEnd_PutsStartOnTheLeftAndEndOnTheRight()
    {
        // Arrange
        const string startText = "Back";
        const string endText = "Save";
        const string extraClass = "mt-3";

        // Act
        var cut = Render<AdminActions>(p => p.Add(x => x.Start, Markup($"<a data-x>{startText}</a>")).Add(x => x.End, Markup($"<button data-x>{endText}</button>"))
            .Add(x => x.Class, extraClass));

        // Assert
        var bar = cut.Find("[data-actions]");
        Assert.Equal(
            (true, true, startText, endText, true),
            (bar.ClassList.Contains("justify-content-between"), bar.ClassList.Contains(extraClass),
                cut.Find("[data-actions-start]").TextContent, cut.Find("[data-actions-end]").TextContent,
                cut.Find("[data-actions-end]").ClassList.Contains("ms-auto")));
    }

    [Fact]
    public void Render_ActionsWithMiddle_PutsItBetweenStartAndEndInAColumnThatGrowsAndWraps()
    {
        // Act
        var cut = Render<AdminActions>(p => p.Add(x => x.Start, Markup("<a>Back</a>")).Add(x => x.Middle, Markup("<span>An error</span>"))
            .Add(x => x.End, Markup("<button>Save</button>")));

        // Assert
        var middle = cut.Find("[data-actions-middle]");
        Assert.Equal(
            ("data-actions-start data-actions-middle data-actions-end", "An error", "flex: 1 1 12rem; min-width: 0", "d-flex flex-column gap-1"),
            (string.Join(' ', cut.Find("[data-actions]").Children.Select(static child => child.Attributes.Single(static a => a.Name.StartsWith("data-actions-", StringComparison.Ordinal)).Name)),
                middle.TextContent, middle.GetAttribute("style"), middle.ClassName));
    }

    [Fact]
    public void Render_ActionsWithNoMiddle_HasNoMiddleArea()
    {
        // Act
        var cut = Render<AdminActions>(p => p.Add(x => x.Start, Markup("<a>Back</a>")).Add(x => x.End, Markup("<button>Save</button>")));

        // Assert
        Assert.Empty(cut.FindAll("[data-actions-middle]"));
    }

    [Fact]
    public void Render_ClosedDialog_ShowsNothing()
    {
        // Act
        var cut = Render<AdminDialog>(p => p.Add(x => x.Open, false).Add(x => x.Title, "New"));

        // Assert
        Assert.Empty(cut.Nodes);
    }

    [Fact]
    public void Render_OpenDialog_ShowsTheTitleTheBodyAndTheActionsInTheFooter()
    {
        // Arrange
        const string titleText = "New template";
        const string bodyText = "Fields";
        const string startText = "Cancel";
        const string endText = "Open the editor";

        // Act
        var cut = Render<AdminDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, titleText)
            .AddChildContent($"<p data-body>{bodyText}</p>")
            .Add(x => x.Start, Markup($"<button>{startText}</button>")).Add(x => x.End, Markup($"<button>{endText}</button>")));

        // Assert
        var title = cut.Find(".modal-title");
        Assert.Equal(
            (titleText, title.Id, bodyText, startText, endText, false),
            (title.TextContent, cut.Find("[data-dialog]").GetAttribute("aria-labelledby"), cut.Find(".modal-body [data-body]").TextContent,
                cut.Find(".modal-footer [data-actions-start]").TextContent, cut.Find(".modal-footer [data-actions-end]").TextContent,
                cut.Find(".modal-dialog").ClassList.Contains("modal-lg")));
        Assert.Single(cut.FindAll(".modal-backdrop"));
    }

    [Fact]
    public void Render_OpenDialogWithNoActions_HasNoFooter()
    {
        // Act
        var cut = Render<AdminDialog>(p => p.Add(x => x.Open, true).AddChildContent("<div data-actions>Own bar</div>"));

        // Assert
        Assert.Equal((0, "Own bar"), (cut.FindAll(".modal-footer").Count, cut.Find(".modal-body [data-actions]").TextContent));
    }

    [Fact]
    public void Render_OpenDialog_HasABodyThatIsAFlexColumn()
    {
        // Act
        var cut = Render<AdminDialog>(p => p.Add(x => x.Open, true));

        // Assert
        Assert.Equal((true, true), (cut.Find(".modal-body").ClassList.Contains("d-flex"), cut.Find(".modal-body").ClassList.Contains("flex-column")));
    }

    [Fact]
    public void Render_WideDialog_HasTheLargeSize()
    {
        // Act
        var cut = Render<AdminDialog>(p => p.Add(x => x.Open, true).Add(x => x.Wide, true));

        // Assert
        Assert.Contains("modal-lg", cut.Find(".modal-dialog").ClassList);
    }

    private static RenderFragment Markup(string html) => builder => builder.AddMarkupContent(0, html);
}
