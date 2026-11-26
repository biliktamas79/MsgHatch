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
        // Arrange
        const string newText = "ab";
        var texts = new List<string>();
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup()).Add(x => x.Text, "a").Add(x => x.TextChanged, texts.Add));

        // Act
        cut.Find("textarea").Input(newText);

        // Assert
        Assert.Equal([newText], texts);
    }

    [Fact]
    public void Render_Disabled_DisablesTheTextArea()
    {
        // Act
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup()).Add(x => x.Disabled, true));

        // Assert
        Assert.True(cut.Find("textarea").HasAttribute("disabled"));
    }

    [Fact]
    public void Render_IssuesWithAndWithNoPosition_ShowsEachIssueUnderTheText()
    {
        // Arrange
        const string tagMessage = "The tag has no end.";
        const string filterMessage = "The filter is not known.";
        IReadOnlyList<TemplateIssue> issues = [new TemplateIssue(tagMessage, 3, 9), new TemplateIssue(filterMessage, null, null)];

        // Act
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup()).Add(x => x.Issues, issues));

        // Assert
        Assert.Equal([$"Line 3, column 9: {tagMessage}", filterMessage], cut.FindAll("[data-issue]").Select(static issue => issue.TextContent));
        Assert.Contains("is-invalid", cut.Find("textarea").ClassList);
    }

    [Theory]
    [InlineData(TemplatePartKind.PlainText, false, false, "")]
    [InlineData(TemplatePartKind.PlainText, true, false, "border-warning")]
    [InlineData(TemplatePartKind.PlainText, true, true, "is-invalid")]
    [InlineData(TemplatePartKind.Html, true, false, "border-warning")]
    public void Render_ChangedText_MarksTheTextAreaWithAYellowBorderUnlessTheTextHasAnIssue(TemplatePartKind kind, bool changed, bool issue, string expected)
    {
        // Arrange
        IReadOnlyList<TemplateIssue> issues = issue ? [new TemplateIssue("The tag has no end.", null, null)] : [];

        // Act
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup(part: "body", kind: kind)).Add(x => x.Changed, changed).Add(x => x.Issues, issues));

        // Assert
        Assert.Equal(expected, string.Join(' ', cut.Find("textarea").ClassList.Where(static name => name is "border-warning" or "is-invalid")));
    }

    [Fact]
    public void Render_HtmlPart_HighlightsTheHtmlOverTheTextArea()
    {
        // Act
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup(part: "body", kind: TemplatePartKind.Html)).Add(x => x.Text, _html));

        // Assert
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
        // Act
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup()).Add(x => x.Text, _html));

        // Assert
        Assert.Empty(cut.FindAll("pre"));
        Assert.Equal("plain", cut.Find("textarea").GetAttribute("data-editor"));
    }

    [Fact]
    public void Input_HtmlPart_GivesTheTextToTextChanged()
    {
        // Arrange
        const string newText = "<p>a";
        var texts = new List<string>();
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup(part: "body", kind: TemplatePartKind.Html))
            .Add(x => x.Text, "<p>").Add(x => x.TextChanged, texts.Add));

        // Act
        cut.Find("textarea").Input(newText);

        // Assert
        Assert.Equal([newText], texts);
    }

    [Fact]
    public void Render_HtmlPartWithAnIssueAndDisabled_MarksTheTextAreaAndShowsTheIssue()
    {
        // Arrange
        const string message = "The tag has no end.";
        IReadOnlyList<TemplateIssue> issues = [new TemplateIssue(message, null, null)];

        // Act
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup(part: "body", kind: TemplatePartKind.Html))
            .Add(x => x.Disabled, true).Add(x => x.Issues, issues));

        // Assert
        Assert.Contains("is-invalid", cut.Find("textarea").ClassList);
        Assert.True(cut.Find("textarea").HasAttribute("disabled"));
        Assert.Equal(message, cut.Find("[data-issue]").TextContent);
    }
}
