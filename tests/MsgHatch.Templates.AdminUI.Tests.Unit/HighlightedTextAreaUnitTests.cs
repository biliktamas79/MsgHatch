using Bunit;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class HighlightedTextAreaUnitTests : BunitContext
{
    private const string _module = "./_content/MsgHatch.Templates.AdminUI/highlightedTextArea.js";

    [Fact]
    public void Render_SegmentsWithAndWithNoMark_PutsEachSegmentIntoTheLayerWithItsClass()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Act
        var cut = Render<HighlightedTextArea>(p => p.Add(x => x.Text, "xy")
            .Add(x => x.Segments, [new TextSegment(0, 1, "a"), new TextSegment(1, 1, "b", "1:2")]));

        // Assert
        Assert.Equal("x", cut.Find("pre span.a").TextContent);
        var mark = cut.Find("pre mark.template-issue.b");
        Assert.Equal("y", mark.TextContent);
        Assert.Equal("1:2", mark.GetAttribute("data-mark"));
        Assert.Equal("xy", cut.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void Render_FirstTime_ImportsTheModuleAndKeepsTheScrollOfTheLayersInStep()
    {
        // Arrange
        var module = JSInterop.SetupModule(_module);
        module.SetupVoid("syncScroll", static _ => true).SetVoidResult();

        // Act
        Render<HighlightedTextArea>(p => p.Add(x => x.Text, "x").Add(x => x.Segments, [new TextSegment(0, 1, "a")]));

        // Assert
        module.VerifyInvoke("syncScroll");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Render_ReadOnly_SetsTheReadOnlyAttributeOfTheTextAreaOnlyWhenTrue(bool readOnly)
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Act
        var cut = Render<HighlightedTextArea>(p => p.Add(x => x.Text, "x").Add(x => x.Segments, [new TextSegment(0, 1, "a")]).Add(x => x.ReadOnly, readOnly));

        // Assert
        Assert.Equal((readOnly, false), (cut.Find("textarea").HasAttribute("readonly"), cut.Find("textarea").HasAttribute("disabled")));
    }

    [Theory]
    [InlineData(false, false, "")]
    [InlineData(true, false, "border-warning")]
    [InlineData(true, true, "is-invalid")]
    public void Render_Changed_MarksTheTextAreaWithAYellowBorderUnlessItIsInvalid(bool changed, bool invalid, string expected)
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Act
        var cut = Render<HighlightedTextArea>(p => p.Add(x => x.Text, "x").Add(x => x.Segments, [new TextSegment(0, 1, "a")]).Add(x => x.Changed, changed)
            .Add(x => x.Invalid, invalid));

        // Assert
        Assert.Equal(expected, string.Join(' ', cut.Find("textarea").ClassList.Where(static name => name is "border-warning" or "is-invalid")));
    }

    [Fact]
    public void Input_NewText_GivesTheTextToTextChanged()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;
        var texts = new List<string>();
        var cut = Render<HighlightedTextArea>(p => p.Add(x => x.Text, "a").Add(x => x.Segments, [new TextSegment(0, 1, "a")])
            .Add(x => x.TextChanged, texts.Add));

        // Act
        cut.Find("textarea").Input("ab");

        // Assert
        Assert.Equal(["ab"], texts);
    }
}
