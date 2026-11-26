using Bunit;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class AdminPagerUnitTests : BunitContext
{
    [Fact]
    public void Render_OnePage_ShowsNoPager()
    {
        // Act
        var cut = Render<AdminPager>(p => p.Add(x => x.PageCount, 1));

        // Assert
        Assert.Empty(cut.FindAll("nav"));
    }

    [Fact]
    public void Render_FirstOfThreePages_DisablesPreviousAndShowsTheNumberOfThePage()
    {
        // Act
        var cut = Render<AdminPager>(p => p.Add(x => x.Page, 0).Add(x => x.PageCount, 3));

        // Assert
        Assert.True(cut.Find("[data-page=previous]").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-page=next]").HasAttribute("disabled"));
        Assert.Contains("Page 1 of 3", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ClickNext_SecondOfThreePages_GivesTheThirdPage()
    {
        // Arrange
        var pages = new List<int>();
        var cut = Render<AdminPager>(p => p.Add(x => x.Page, 1).Add(x => x.PageCount, 3).Add(x => x.PageChanged, pages.Add));

        // Act
        cut.Find("[data-page=next]").Click();

        // Assert
        Assert.Equal([2], pages);
    }

    [Fact]
    public void ClickPrevious_SecondOfThreePages_GivesTheFirstPage()
    {
        // Arrange
        var pages = new List<int>();
        var cut = Render<AdminPager>(p => p.Add(x => x.Page, 1).Add(x => x.PageCount, 3).Add(x => x.PageChanged, pages.Add));

        // Act
        cut.Find("[data-page=previous]").Click();

        // Assert
        Assert.Equal([0], pages);
    }
}
