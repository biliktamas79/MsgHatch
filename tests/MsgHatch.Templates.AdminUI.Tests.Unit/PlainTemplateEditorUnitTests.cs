using Bunit;
using static MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes.TemplateTestData;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class PlainTemplateEditorUnitTests : BunitContext
{
    private const string _html = "<p class=\"a\">Hi &amp; bye</p>";

    public PlainTemplateEditorUnitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Input_NewText_GivesTheTextToTextChanged()
    {
        var texts = new List<string>();
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup()).Add(x => x.Text, "a").Add(x => x.TextChanged, texts.Add));

        cut.Find("textarea").Input("ab");

        Assert.Equal(["ab"], texts);
    }

    [Fact]
    public void Render_Disabled_DisablesTheTextArea()
    {
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup()).Add(x => x.Disabled, true));

        Assert.True(cut.Find("textarea").HasAttribute("disabled"));
    }

    [Fact]
    public void Render_IssuesWithAndWithNoPosition_ShowsEachIssueUnderTheText()
    {
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup())
            .Add(x => x.Issues, [new TemplateIssue("The tag has no end.", 3, 9), new TemplateIssue("The filter is not known.", null, null)]));

        Assert.Equal(["Line 3, column 9: The tag has no end.", "The filter is not known."], cut.FindAll("[data-issue]").Select(static issue => issue.TextContent));
        Assert.Contains("is-invalid", cut.Find("textarea").ClassList);
    }

    [Fact]
    public void Render_HtmlPart_HighlightsTheHtmlOverTheTextArea()
    {
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup(part: "body", kind: TemplatePartKind.Html)).Add(x => x.Text, _html));

        Assert.Equal(["p", "p"], cut.FindAll(".html-tagname").Select(static span => span.TextContent));
        Assert.Equal("class", cut.Find(".html-attribute").TextContent);
        Assert.Equal("\"a\"", cut.Find(".html-value").TextContent);
        Assert.Equal("&amp;", cut.Find(".html-entity").TextContent);
        Assert.Equal(_html + "\n", cut.Find("pre").TextContent);
        Assert.Single(cut.FindAll("[data-editor=plain]"));
    }

    [Fact]
    public void Render_PlainTextPart_ShowsATextAreaWithNoHighlight()
    {
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup()).Add(x => x.Text, _html));

        Assert.Empty(cut.FindAll("pre"));
        Assert.Equal("plain", cut.Find("textarea").GetAttribute("data-editor"));
    }

    [Fact]
    public void Input_HtmlPart_GivesTheTextToTextChanged()
    {
        var texts = new List<string>();
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup(part: "body", kind: TemplatePartKind.Html))
            .Add(x => x.Text, "<p>").Add(x => x.TextChanged, texts.Add));

        cut.Find("textarea").Input("<p>a");

        Assert.Equal(["<p>a"], texts);
    }

    [Fact]
    public void Render_HtmlPartWithAnIssueAndDisabled_MarksTheTextAreaAndShowsTheIssue()
    {
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup(part: "body", kind: TemplatePartKind.Html))
            .Add(x => x.Disabled, true).Add(x => x.Issues, [new TemplateIssue("The tag has no end.", null, null)]));

        Assert.Contains("is-invalid", cut.Find("textarea").ClassList);
        Assert.True(cut.Find("textarea").HasAttribute("disabled"));
        Assert.Equal("The tag has no end.", cut.Find("[data-issue]").TextContent);
    }
}
