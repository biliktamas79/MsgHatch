using Bunit;
using MsgHatch.Domain.Failures;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class AdminListUnitTests : BunitContext
{
    [Fact]
    public void Render_NoItemsYet_ShowsTheLoadingState()
    {
        // Act
        var cut = RenderList(null);

        // Assert
        Assert.Single(cut.FindAll("[data-state=loading]"));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        // Act
        var cut = RenderList(new Error(Errors.Rejected));

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_EmptyList_ShowsTheEmptyText()
    {
        // Arrange
        const string emptyText = "There are no templates.";

        // Act
        var cut = RenderList(Items(0), emptyText: emptyText);

        // Assert
        Assert.Equal(emptyText, cut.Find("[data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_ThreeItems_ShowsTheHeadAndARowForEachItem()
    {
        // Act
        var cut = RenderList(Items(3));

        // Assert
        Assert.Equal("Name", cut.Find("thead th").TextContent);
        Assert.Equal(["item0", "item1", "item2"], cut.FindAll("tbody td").Select(static cell => cell.TextContent));
        Assert.Empty(cut.FindAll("nav"));
    }

    [Fact]
    public void Render_ThirtyItems_ShowsTheFirstPageOf25Items()
    {
        // Act
        var cut = RenderList(Items(30));

        // Assert
        Assert.Equal(25, cut.FindAll("tbody tr").Count);
        Assert.Contains("Page 1 of 2", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ThirtyItems_PutsTheTableInTheScrollAreaAndThePagerUnderIt()
    {
        // Act
        var cut = RenderList(Items(30));

        // Assert
        var scroll = cut.Find("[data-list-scroll]");
        var pager = cut.Find("[data-list-pager]");
        Assert.Equal(
            (true, true, false, true),
            (scroll.QuerySelector("table thead th") is not null, pager.QuerySelector("nav") is not null, scroll.Contains(pager),
                scroll.NextElementSibling?.HasAttribute("data-list-pager") == true));
    }

    [Fact]
    public void Render_ThirtyItems_FramesTheScrollAreaWithARoundedBorder()
    {
        // Act
        var cut = RenderList(Items(30));

        // Assert
        var scroll = cut.Find("[data-list-scroll]");
        Assert.Equal((true, true), (scroll.ClassList.Contains("border"), scroll.ClassList.Contains("rounded")));
    }

    [Fact]
    public void ClickNext_ThirtyItems_ShowsTheLastFiveItems()
    {
        // Arrange
        var cut = RenderList(Items(30));

        // Act
        cut.Find("[data-page=next]").Click();

        // Assert
        Assert.Equal(["item25", "item26", "item27", "item28", "item29"], cut.FindAll("tbody td").Select(static cell => cell.TextContent));
    }

    [Fact]
    public void SetItems_FewerItemsThanTheShownPage_ShowsTheLastPageThatExists()
    {
        // Arrange
        var cut = RenderList(Items(30));
        cut.Find("[data-page=next]").Click();

        // Act
        cut.Render(p => p.Add(x => x.Items, Items(3)));

        // Assert
        Assert.Equal(3, cut.FindAll("tbody tr").Count);
    }

    [Fact]
    public void Render_ThirtyItemsWithAPager_ShowsEachItemAndThePagerInPlaceOfThePageNumbers()
    {
        // Act
        var cut = RenderList(Items(30), pager: "<button data-server-pager>Older</button>");

        // Assert
        Assert.Equal(
            (30, true, false),
            (cut.FindAll("tbody tr").Count, cut.Find("[data-list-pager]").QuerySelector("[data-server-pager]") is not null,
                cut.Markup.Contains("Page 1 of", StringComparison.Ordinal)));
    }

    [Fact]
    public void Render_EmptyListWithAPager_ShowsThePagerUnderTheEmptyText()
    {
        // Act
        var cut = RenderList(Items(0), pager: "<button data-server-pager>Newer</button>");

        // Assert
        Assert.Equal(
            (true, true),
            (cut.Find("[data-state=empty]") is not null, cut.Find("[data-list-pager]").QuerySelector("[data-server-pager]") is not null));
    }

    private static ErrorOr<IReadOnlyList<string>> Items(int count) =>
        ErrorOr<IReadOnlyList<string>>.ForValue([.. Enumerable.Range(0, count).Select(static index => $"item{index}")]);

    private IRenderedComponent<AdminList<string>> RenderList(ErrorOr<IReadOnlyList<string>>? items, string? emptyText = null, string? pager = null) =>
        Render<AdminList<string>>(p =>
        {
            p.Add(x => x.Items, items)
                .Add(x => x.Head, "<tr><th>Name</th></tr>")
                .Add(x => x.Row, static item => $"<tr><td>{item}</td></tr>");
            if (emptyText is not null)
            {
                p.Add(x => x.EmptyText, emptyText);
            }

            if (pager is not null)
            {
                p.Add(x => x.Pager, pager);
            }
        });
}
