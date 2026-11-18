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
        const string text = "{% if x %}Hi {{ Name | upcase }}{% endif %}";

        var cut = RenderEditor(text);

        Assert.Equal(["if", "endif"], cut.FindAll(".liquid-tagname").Select(static span => span.TextContent));
        Assert.Equal("upcase", cut.Find(".liquid-filtername").TextContent);
        Assert.Equal(text + "\n", cut.Find("pre").TextContent);
    }

    [Fact]
    public void Render_OpenOutput_MarksItWithNoCall()
    {
        var cut = RenderEditor("a {{ x");

        Assert.Equal("1:3", cut.Find("mark.liquid-issue").GetAttribute("data-mark"));
        Assert.Equal("Line 1, column 3: " + LiquidTokenizer.OpenOutputMessage, cut.Find("[data-issue]").TextContent);
        _queries.DidNotReceiveWithAnyArgs().CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_LiquidTag_MarksItWithNoCall()
    {
        var cut = RenderEditor("a\n{% liquid echo 1 %}");

        Assert.Equal("2:1", cut.Find("mark.liquid-issue").GetAttribute("data-mark"));
        _queries.DidNotReceiveWithAnyArgs().CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Input_NewText_GivesTheTextToTextChanged()
    {
        var texts = new List<string>();
        var cut = RenderEditor("a", texts.Add);

        cut.Find("textarea").Input("ab");

        Assert.Equal(["ab"], texts);
    }

    [Fact]
    public void Input_ThreeChangesAndAPause_CallsTheCheckOneTimeWithTheLastText()
    {
        var cut = RenderEditor("a");

        cut.Find("textarea").Input("ab");
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Find("textarea").Input("abc");
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Find("textarea").Input("abcd");
        _clock.Advance(TimeSpan.FromMilliseconds(499));
        _queries.DidNotReceiveWithAnyArgs().CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>());

        _clock.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => _queries.ReceivedWithAnyArgs(1).CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>()));
        _queries.Received(1).CheckTemplateAsync(_lookup, "abcd", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Input_CheckGivesAnIssueWithAPosition_MarksTheLineAndTheColumn()
    {
        GivenCheck([new TemplateIssue("The tag is not known.", 2, 4)]);
        var cut = RenderEditor("ab\ncd x");

        cut.Find("textarea").Input("ab\ncd x");
        _clock.Advance(TimeSpan.FromMilliseconds(500));

        cut.WaitForAssertion(() => Assert.Equal("2:4", cut.Find("mark.liquid-issue").GetAttribute("data-mark")));
        Assert.Equal("x", cut.Find("mark.liquid-issue").TextContent);
        Assert.Equal("Line 2, column 4: The tag is not known.", cut.Find("[data-issue]").TextContent);
    }

    [Fact]
    public void Input_CheckGivesAnIssueWithNoPosition_ShowsOnlyItsMessage()
    {
        GivenCheck([new TemplateIssue("The filter shout is not known.", null, null)]);
        var cut = RenderEditor("{{ x | shout }}");

        cut.Find("textarea").Input("{{ x | shout }}");
        _clock.Advance(TimeSpan.FromMilliseconds(500));

        cut.WaitForAssertion(() => Assert.Equal("The filter shout is not known.", cut.Find("[data-issue]").TextContent));
        Assert.Empty(cut.FindAll("mark"));
    }

    [Fact]
    public void Input_CheckUnavailable_ShowsThatTheCheckDidNotRun()
    {
        _queries.CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForError(new Error(Errors.Unavailable))));
        var cut = RenderEditor("a");

        cut.Find("textarea").Input("ab");
        _clock.Advance(TimeSpan.FromMilliseconds(500));

        cut.WaitForAssertion(() => Assert.Contains(AdminErrors.UnavailableText, cut.Find("[data-state=check-failed]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Render_IssueOfThePage_ShowsItsMessage()
    {
        var cut = Render<LiquidTemplateEditor>(p => p.Add(x => x.Lookup, _lookup).Add(x => x.Text, "a")
            .Add(x => x.Issues, [new TemplateIssue("The template is not valid.", null, null)]));

        Assert.Equal("The template is not valid.", cut.Find("[data-issue]").TextContent);
    }

    [Fact]
    public async Task CanSaveAsync_CheckGivesAnIssue_GivesFalseAndShowsTheIssue()
    {
        GivenCheck([new TemplateIssue("The tag has no end.", 1, 1)]);
        var cut = RenderEditor("{% if x %}");

        var canSave = await cut.InvokeAsync(() => cut.Instance.CanSaveAsync(Xunit.TestContext.Current.CancellationToken).AsTask());

        Assert.False(canSave);
        _ = _queries.Received(1).CheckTemplateAsync(_lookup, "{% if x %}", Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Assert.Equal("Line 1, column 1: The tag has no end.", cut.Find("[data-issue]").TextContent));
    }

    [Fact]
    public async Task CanSaveAsync_CheckGivesNoIssue_GivesTrue()
    {
        var cut = RenderEditor("Hi {{ Name }}");

        var canSave = await cut.InvokeAsync(() => cut.Instance.CanSaveAsync(Xunit.TestContext.Current.CancellationToken).AsTask());

        Assert.True(canSave);
    }

    [Fact]
    public async Task CanSaveAsync_CheckUnavailable_GivesTrueSoThatTheSaveGivesItsError()
    {
        _queries.CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForError(new Error(Errors.Unavailable))));
        var cut = RenderEditor("Hi");

        var canSave = await cut.InvokeAsync(() => cut.Instance.CanSaveAsync(Xunit.TestContext.Current.CancellationToken).AsTask());

        Assert.True(canSave);
    }

    [Fact]
    public void EngineName_OfTheEditor_EqualsTheNameOfTheLiquidEngine()
    {
        Assert.Equal(LiquidTemplateEditor.EngineName, LiquidTemplates.Engine.Name);
    }

    private void GivenCheck(IReadOnlyList<TemplateIssue> issues) =>
        _queries.CheckTemplateAsync(default, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForValue(issues)));

    private IRenderedComponent<LiquidTemplateEditor> RenderEditor(string text, Action<string>? textChanged = null) =>
        Render<LiquidTemplateEditor>(p => p.Add(x => x.Lookup, _lookup).Add(x => x.Text, text).Add(x => x.TextChanged, textChanged ?? (static _ => { })));
}
