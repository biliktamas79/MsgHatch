using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Management;
using NSubstitute;

namespace MsgHatch.Templates.Liquid.AdminUI.Tests.Unit;

public sealed class LiquidTemplateEditorUnitTests : BunitContext
{
    private static readonly TemplateLookup _lookup = new(new MessageType("Requests"), "en", null, "body", TemplatePartKind.Html);
    private static readonly TemplateLookup _subject = new(new MessageType("Requests"), "en", null, "subject", TemplatePartKind.PlainText);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ITemplateQueries _queries = Substitute.For<ITemplateQueries>();

    public LiquidTemplateEditorUnitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddKeyedSingleton(TenantKey.None, _queries);
        GivenCheck([]);
    }

    [Fact]
    public void Render_Text_HighlightsTheTagNamesAndKeepsEachCharacterInTheLayer()
    {
        // Arrange
        const string text = "{% if x %}Hi {{ Name | upcase }}{% endif %}";

        // Act
        var cut = RenderEditor(text);

        // Assert
        Assert.Equal(["if", "endif"], cut.FindAll(".liquid-tagname").Select(static span => span.TextContent));
        Assert.Equal("upcase", cut.Find(".liquid-filtername").TextContent);
        Assert.Equal(text + "\n", cut.Find("pre").TextContent);
    }

    [Fact]
    public void Render_EachKindOfSpan_GetsTheClassOfItsKind()
    {
        // Act
        var cut = RenderEditor("t{{ 'q' | plus: 1 }}{% comment %}c{% endcomment %}{% raw %}r{% endraw %}", lookup: _subject);

        // Assert
        Assert.Equal("'q'", cut.Find(".liquid-quoted").TextContent);
        Assert.Equal("1", cut.Find(".liquid-number").TextContent);
        Assert.Equal("c", cut.Find(".liquid-comment").TextContent);
        Assert.Equal("r", cut.Find(".liquid-raw").TextContent);
        Assert.Equal("t", cut.Find(".liquid-text").TextContent);
        Assert.NotEmpty(cut.FindAll(".liquid-code"));
        Assert.NotEmpty(cut.FindAll(".liquid-delimiter"));
    }

    [Fact]
    public void Render_HtmlPart_HighlightsTheHtmlInTheTextSpans()
    {
        // Act
        var cut = RenderEditor("<a href=\"{{ Link }}\">{{ Name }}</a>");

        // Assert
        Assert.Equal(["a", "a"], cut.FindAll(".html-tagname").Select(static span => span.TextContent));
        Assert.Equal("href", cut.Find(".html-attribute").TextContent);
        Assert.Equal(["\"", "\""], cut.FindAll(".html-value").Select(static span => span.TextContent));
        Assert.Equal(4, cut.FindAll(".liquid-delimiter").Count);
    }

    [Fact]
    public void Render_HtmlPartWithALessThanInLiquidCode_StartsNoTagThere()
    {
        // Act
        var cut = RenderEditor("{{ a<b }}<i>x</i>");

        // Assert
        Assert.Equal(["i", "i"], cut.FindAll(".html-tagname").Select(static span => span.TextContent));
    }

    [Fact]
    public void Render_PlainTextPart_HighlightsNoHtml()
    {
        // Act
        var cut = RenderEditor("<p>{{ x }}</p>", lookup: _subject);

        // Assert
        Assert.Empty(cut.FindAll(".html-tagname"));
        Assert.Equal("<p>", cut.Find(".liquid-text").TextContent);
    }

    [Fact]
    public void Render_OpenOutput_MarksItWithNoCall()
    {
        var cut = RenderEditor("a {{ x");

        Assert.Equal("1:3", cut.Find("mark.template-issue").GetAttribute("data-mark"));
        Assert.Equal("Line 1, column 3: " + LiquidTokenizer.OpenOutputMessage, cut.Find("[data-issue]").TextContent);
        _queries.DidNotReceiveWithAnyArgs().CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_LiquidTag_MarksItWithNoCall()
    {
        // Act
        var cut = RenderEditor("a\n{% liquid echo 1 %}");

        // Assert
        Assert.Equal("2:1", cut.Find("mark.template-issue").GetAttribute("data-mark"));
        _queries.DidNotReceiveWithAnyArgs().CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Input_NewText_GivesTheTextToTextChanged()
    {
        // Arrange
        const string newText = "ab";
        var texts = new List<string>();
        var cut = RenderEditor("a", texts.Add);

        // Act
        cut.Find("textarea").Input(newText);

        // Assert
        Assert.Equal([newText], texts);
    }

    [Fact]
    public void Input_ThreeChangesAndAPause_CallsTheCheckOneTimeWithTheLastText()
    {
        // Arrange
        const string firstText = "ab";
        const string secondText = "abc";
        const string lastText = "abcd";
        var cut = RenderEditor("a");

        // Act
        cut.Find("textarea").Input(firstText);
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Find("textarea").Input(secondText);
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Find("textarea").Input(lastText);
        _clock.Advance(TimeSpan.FromMilliseconds(499));
        _queries.DidNotReceiveWithAnyArgs().CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>());

        _clock.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        cut.WaitForAssertion(() => _queries.Received(1).CheckTemplateAsync(_lookup, lastText, Arg.Any<CancellationToken>()));
        _queries.DidNotReceive().CheckTemplateAsync(_lookup, firstText, Arg.Any<CancellationToken>());
        _queries.DidNotReceive().CheckTemplateAsync(_lookup, secondText, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Input_CheckGivesAnIssueWithAPosition_MarksTheLineAndTheColumn()
    {
        // Arrange
        const string text = "ab\ncd x";
        const string message = "The tag is not known.";
        GivenCheck([new TemplateIssue(message, 2, 4)]);
        var cut = RenderEditor(text);

        // Act
        cut.Find("textarea").Input(text);
        _clock.Advance(TimeSpan.FromMilliseconds(500));

        // Assert
        cut.WaitForAssertion(() => Assert.Equal("2:4", cut.Find("mark.template-issue").GetAttribute("data-mark")));
        Assert.Equal("x", cut.Find("mark.template-issue").TextContent);
        Assert.Equal($"Line 2, column 4: {message}", cut.Find("[data-issue]").TextContent);
        _queries.Received(1).CheckTemplateAsync(_lookup, text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Input_CheckGivesAnIssueWithNoPosition_ShowsOnlyItsMessage()
    {
        // Arrange
        const string text = "{{ x | shout }}";
        const string message = "The filter shout is not known.";
        GivenCheck([new TemplateIssue(message, null, null)]);
        var cut = RenderEditor(text);

        // Act
        cut.Find("textarea").Input(text);
        _clock.Advance(TimeSpan.FromMilliseconds(500));

        // Assert
        cut.WaitForAssertion(() => Assert.Equal(message, cut.Find("[data-issue]").TextContent));
        Assert.Empty(cut.FindAll("mark"));
        _queries.Received(1).CheckTemplateAsync(_lookup, text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Input_CheckGivesAnIssueAfterTheEndOfTheText_ShowsOnlyItsMessage()
    {
        // Arrange
        const string text = "a";
        const string message = "The tag has no end.";
        GivenCheck([new TemplateIssue(message, 5, 1)]);
        var cut = RenderEditor(text);

        // Act
        cut.Find("textarea").Input(text);
        _clock.Advance(TimeSpan.FromMilliseconds(500));

        // Assert
        cut.WaitForAssertion(() => Assert.Equal($"Line 5, column 1: {message}", cut.Find("[data-issue]").TextContent));
        Assert.Empty(cut.FindAll("mark"));
        _queries.Received(1).CheckTemplateAsync(_lookup, text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Input_CheckUnavailable_ShowsThatTheCheckDidNotRun()
    {
        // Arrange
        const string newText = "ab";
        _queries.CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForError(new Error(Errors.Unavailable))));
        var cut = RenderEditor("a");

        // Act
        cut.Find("textarea").Input(newText);
        _clock.Advance(TimeSpan.FromMilliseconds(500));

        // Assert
        cut.WaitForAssertion(() => Assert.Contains(AdminErrors.UnavailableText, cut.Find("[data-state=check-failed]").TextContent, StringComparison.Ordinal));
        _queries.Received(1).CheckTemplateAsync(_lookup, newText, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_IssueOfThePage_ShowsItsMessage()
    {
        // Arrange
        const string message = "The template is not valid.";
        IReadOnlyList<TemplateIssue> issues = [new TemplateIssue(message, null, null)];

        // Act
        var cut = Render<LiquidTemplateEditor>(p => p.Add(x => x.Lookup, _lookup).Add(x => x.Text, "a").Add(x => x.Issues, issues));

        // Assert
        Assert.Equal(message, cut.Find("[data-issue]").TextContent);
    }

    [Fact]
    public async Task CanSaveAsync_CheckGivesAnIssue_GivesFalseAndShowsTheIssue()
    {
        // Arrange
        const string text = "{% if x %}";
        const string message = "The tag has no end.";
        GivenCheck([new TemplateIssue(message, 1, 1)]);
        var cut = RenderEditor(text);

        // Act
        var canSave = await cut.InvokeAsync(() => cut.Instance.CanSaveAsync(Xunit.TestContext.Current.CancellationToken).AsTask());

        // Assert
        Assert.False(canSave);
        _ = _queries.Received(1).CheckTemplateAsync(_lookup, text, Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Assert.Equal($"Line 1, column 1: {message}", cut.Find("[data-issue]").TextContent));
    }

    [Fact]
    public async Task CanSaveAsync_CheckGivesNoIssue_GivesTrue()
    {
        // Arrange
        const string text = "Hi {{ Name }}";
        var cut = RenderEditor(text);

        // Act
        var canSave = await cut.InvokeAsync(() => cut.Instance.CanSaveAsync(Xunit.TestContext.Current.CancellationToken).AsTask());

        // Assert
        Assert.True(canSave);
        _ = _queries.Received(1).CheckTemplateAsync(_lookup, text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CanSaveAsync_CheckUnavailable_GivesTrueSoThatTheSaveGivesItsError()
    {
        // Arrange
        _queries.CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForError(new Error(Errors.Unavailable))));
        const string text = "Hi";
        var cut = RenderEditor(text);

        // Act
        var canSave = await cut.InvokeAsync(() => cut.Instance.CanSaveAsync(Xunit.TestContext.Current.CancellationToken).AsTask());

        // Assert
        Assert.True(canSave);
        _ = _queries.Received(1).CheckTemplateAsync(_lookup, text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EngineName_OfTheEditor_EqualsTheNameOfTheLiquidEngine()
    {
        // Act and assert
        Assert.Equal(LiquidTemplateEditor.EngineName, LiquidTemplates.Engine.Name);
    }

    private void GivenCheck(IReadOnlyList<TemplateIssue> issues) =>
        _queries.CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForValue(issues)));

    [Fact]
    public void Render_Changed_MarksTheTextAreaWithAYellowBorder()
    {
        // Act
        var cut = Render<LiquidTemplateEditor>(p => p.Add(x => x.Lookup, _lookup).Add(x => x.Text, "a").Add(x => x.Changed, true));

        // Assert
        Assert.Contains("border-warning", cut.Find("textarea").ClassList);
    }

    private IRenderedComponent<LiquidTemplateEditor> RenderEditor(string text, Action<string>? textChanged = null, TemplateLookup? lookup = null) =>
        Render<LiquidTemplateEditor>(p => p.Add(x => x.Lookup, lookup ?? _lookup).Add(x => x.Text, text).Add(x => x.TextChanged, textChanged ?? (static _ => { })));
}
