using Bunit;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class AdminListPageUnitTests : BunitContext
{
    [Fact]
    public void Render_AllParts_PutsTheTitleTheFiltersLeftTheActionsRightAndTheListUnderThem()
    {
        // Act
        var cut = Render<AdminListPage>(p => p.Add(x => x.Title, "Templates")
            .Add(x => x.Links, builder => builder.AddMarkupContent(0, "<a data-link>Message types</a>"))
            .Add(x => x.Filters, builder => builder.AddMarkupContent(0, "<select data-filter></select>"))
            .Add(x => x.Actions, builder => builder.AddMarkupContent(0, "<button data-new>+ New template</button>"))
            .AddChildContent("<table data-table></table>"));

        // Assert
        var header = cut.Find("[data-list-header]");
        var actions = cut.Find("[data-list-toolbar] [data-list-actions]");
        Assert.Equal(
            ("Templates", true, true, "+ New template", true, true),
            (header.QuerySelector("h1")!.TextContent, header.QuerySelector("[data-link]") is not null,
                cut.Find("[data-list-toolbar] [data-list-filters]").QuerySelector("[data-filter]") is not null, actions.TextContent,
                actions.ClassList.Contains("ms-auto"), cut.Find("[data-list-body]").QuerySelector("[data-table]") is not null));
    }

    [Fact]
    public void Render_Filters_PutsThemInTheFramedFilterBar()
    {
        // Act
        var cut = Render<AdminListPage>(p => p.Add(x => x.Filters, builder => builder.AddMarkupContent(0, "<label for=\"f\">Company id</label><input id=\"f\" />")));

        // Assert
        var bar = cut.Find("[data-list-filters]");
        Assert.Equal((true, "Company id"), (bar.ClassList.Contains("admin-filter-bar"), bar.QuerySelector("label")!.TextContent));
    }

    [Fact]
    public void Render_NoFilters_HasNoFilterBar()
    {
        // Act
        var cut = Render<AdminListPage>(p => p.Add(x => x.Title, "Templates"));

        // Assert
        Assert.Empty(cut.FindAll("[data-list-filters]"));
    }

    [Fact]
    public void Render_Page_IsAColumnThatGrowsInItsHost()
    {
        // Act
        var cut = Render<AdminListPage>(p => p.Add(x => x.Title, "Templates"));

        // Assert
        var page = cut.Find("[data-list-page]");
        Assert.Equal(
            (true, true, true),
            (page.ClassList.Contains("flex-column"), page.ClassList.Contains("flex-grow-1"), cut.Find("[data-list-body]").ClassList.Contains("flex-grow-1")));
    }
}
