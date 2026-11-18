using Bunit;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class HighlightedTextAreaUnitTests : BunitContext
{
    private const string _module = "./_content/MsgHatch.Templates.AdminUI/highlightedTextArea.js";

    [Fact]
    public void Render_SegmentsWithAndWithNoMark_PutsEachSegmentIntoTheLayerWithItsClass()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<HighlightedTextArea>(p => p.Add(x => x.Text, "xy")
            .Add(x => x.Segments, [new TextSegment(0, 1, "a"), new TextSegment(1, 1, "b", "1:2")]));

        Assert.Equal("x", cut.Find("pre span.a").TextContent);
        var mark = cut.Find("pre mark.template-issue.b");
        Assert.Equal("y", mark.TextContent);
        Assert.Equal("1:2", mark.GetAttribute("data-mark"));
        Assert.Equal("xy", cut.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void Render_FirstTime_ImportsTheModuleAndKeepsTheScrollOfTheLayersInStep()
    {
        var module = JSInterop.SetupModule(_module);
        module.SetupVoid("syncScroll", static _ => true).SetVoidResult();

        Render<HighlightedTextArea>(p => p.Add(x => x.Text, "x").Add(x => x.Segments, [new TextSegment(0, 1, "a")]));

        module.VerifyInvoke("syncScroll");
    }

    [Fact]
    public void Input_NewText_GivesTheTextToTextChanged()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var texts = new List<string>();
        var cut = Render<HighlightedTextArea>(p => p.Add(x => x.Text, "a").Add(x => x.Segments, [new TextSegment(0, 1, "a")])
            .Add(x => x.TextChanged, texts.Add));

        cut.Find("textarea").Input("ab");

        Assert.Equal(["ab"], texts);
    }
}
