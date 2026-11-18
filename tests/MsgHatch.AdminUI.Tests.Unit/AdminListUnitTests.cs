using Bunit;
using MsgHatch.Domain.Failures;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class AdminListUnitTests : BunitContext
{
    [Fact]
    public void Render_NoItemsYet_ShowsTheLoadingState()
    {
        var cut = RenderList(null);

        Assert.Single(cut.FindAll("[data-state=loading]"));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        var cut = RenderList(new Error(Errors.Rejected));

        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_EmptyList_ShowsTheEmptyText()
    {
        var cut = RenderList(Items(0), emptyText: "There are no templates.");

        Assert.Equal("There are no templates.", cut.Find("[data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_ThreeItems_ShowsTheHeadAndARowForEachItem()
    {
        var cut = RenderList(Items(3));

        Assert.Equal("Name", cut.Find("thead th").TextContent);
        Assert.Equal(["item0", "item1", "item2"], cut.FindAll("tbody td").Select(static cell => cell.TextContent));
        Assert.Empty(cut.FindAll("nav"));
    }

    [Fact]
    public void Render_ThirtyItems_ShowsTheFirstPageOf25Items()
    {
        var cut = RenderList(Items(30));

        Assert.Equal(25, cut.FindAll("tbody tr").Count);
        Assert.Contains("Page 1 of 2", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ThirtyItems_PutsTheTableInTheScrollAreaAndThePagerUnderIt()
    {
        var cut = RenderList(Items(30));

        var scroll = cut.Find("[data-list-scroll]");
        var pager = cut.Find("[data-list-pager]");
        Assert.Equal(
            (true, true, false, true),
            (scroll.QuerySelector("table thead th") is not null, pager.QuerySelector("nav") is not null, scroll.Contains(pager),
                scroll.NextElementSibling?.HasAttribute("data-list-pager") == true));
    }

    [Fact]
    public void ClickNext_ThirtyItems_ShowsTheLastFiveItems()
    {
        var cut = RenderList(Items(30));

        cut.Find("[data-page=next]").Click();

        Assert.Equal(["item25", "item26", "item27", "item28", "item29"], cut.FindAll("tbody td").Select(static cell => cell.TextContent));
    }

    [Fact]
    public void SetItems_FewerItemsThanTheShownPage_ShowsTheLastPageThatExists()
    {
        var cut = RenderList(Items(30));
        cut.Find("[data-page=next]").Click();

        cut.Render(p => p.Add(x => x.Items, Items(3)));

        Assert.Equal(3, cut.FindAll("tbody tr").Count);
    }

    private static ErrorOr<IReadOnlyList<string>> Items(int count) =>
        ErrorOr<IReadOnlyList<string>>.ForValue([.. Enumerable.Range(0, count).Select(static index => $"item{index}")]);

    private IRenderedComponent<AdminList<string>> RenderList(ErrorOr<IReadOnlyList<string>>? items, string? emptyText = null) =>
        Render<AdminList<string>>(p =>
        {
            p.Add(x => x.Items, items)
                .Add(x => x.Head, "<tr><th>Name</th></tr>")
                .Add(x => x.Row, static item => $"<tr><td>{item}</td></tr>");
            if (emptyText is not null)
            {
                p.Add(x => x.EmptyText, emptyText);
            }
        });
}
