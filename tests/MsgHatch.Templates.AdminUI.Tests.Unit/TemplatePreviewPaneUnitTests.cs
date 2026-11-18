using Bunit;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Templates.Management;
using NSubstitute;
using static MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes.TemplateTestData;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class TemplatePreviewPaneUnitTests : TemplatePageContext
{
    private static readonly TemplateLookup _body = Lookup(part: "body", kind: TemplatePartKind.Html);

    [Fact]
    public void Render_HtmlPart_ShowsThePreviewInAnIframeWithAnEmptySandboxAndTheTextAsSrcdoc()
    {
        GivenPreviews(new TemplatePreview("Email:Requests", "<p>Hello <b>Sample</b></p><script>alert(1)</script>"));

        var cut = RenderPane(_body, "<p>Hello {{Name}}</p>");

        var frame = cut.Find("iframe[data-preview=html]");
        Assert.Equal(
            ("", "<p>Hello <b>Sample</b></p><script>alert(1)</script>"),
            (frame.GetAttribute("sandbox"), frame.GetAttribute("srcdoc")));
        Assert.Empty(cut.FindAll("pre"));
    }

    [Fact]
    public void Render_TextPart_ShowsThePreviewInAPre()
    {
        GivenPreviews(new TemplatePreview("Email:Requests", "Hello <Sample>"));

        var cut = RenderPane(Lookup(), "Hello {{Name}}");

        Assert.Equal("Hello <Sample>", cut.Find("pre[data-preview=text]").TextContent);
        Assert.Empty(cut.FindAll("iframe"));
    }

    [Fact]
    public void Change_RegistrationOfTwo_ShowsThePreviewOfTheSelectedRegistration()
    {
        GivenPreviews(new TemplatePreview("Email:Requests", "From the email"), new TemplatePreview("Push:Requests", "From the push"));
        var cut = RenderPane(Lookup(), "Text");
        var first = cut.Find("pre").TextContent;

        cut.Find("select[data-field=registration]").Change("Push:Requests");

        Assert.Equal(
            ("Email:Requests, Push:Requests", "From the email", "From the push"),
            (string.Join(", ", cut.FindAll("select option").Select(static option => option.TextContent)), first, cut.Find("pre").TextContent));
    }

    [Fact]
    public void Render_OnePreview_ShowsNoSelect()
    {
        GivenPreviews(new TemplatePreview("Email:Requests", "Hello"));

        var cut = RenderPane(Lookup(), "Text");

        Assert.Empty(cut.FindAll("select"));
    }

    [Fact]
    public void Render_PartWithNoSample_ShowsNoSampleDataAndCallsNothing()
    {
        var cut = RenderPane(Lookup(), "Text", hasSample: false);

        Assert.Equal("No sample data", cut.Find("[data-state=no-sample]").TextContent);
        Assert.Empty(Queries.ReceivedCalls());
    }

    [Fact]
    public void Render_NoPreview_ShowsNoSampleData()
    {
        var cut = RenderPane(Lookup(), "Text");

        Assert.Equal("No sample data", cut.Find("[data-state=no-sample]").TextContent);
    }

    [Fact]
    public void Render_TemplateInvalid_ShowsItsMessageInPlaceOfThePreview()
    {
        Queries.PreviewTemplateAsync(default, default!, default)
            .ReturnsForAnyArgs(Fail<IReadOnlyList<TemplatePreview>>(Errors.TemplateInvalid, "The template has an unclosed placeholder."));

        var cut = RenderPane(Lookup(), "Hello {{Name");

        Assert.Equal("The template has an unclosed placeholder.", cut.Find(".alert-danger").TextContent);
        Assert.Empty(cut.FindAll("[data-preview]"));
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        Queries.PreviewTemplateAsync(default, default!, default).ReturnsForAnyArgs(Fail<IReadOnlyList<TemplatePreview>>(Errors.Unavailable));

        var cut = RenderPane(Lookup(), "Text");

        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void SetText_ThreeChangesAndAPause_CallsThePreviewOneTimeForTheFirstTextAndOneTimeForTheLastText()
    {
        var cut = RenderPane(Lookup(), "a");
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "a", Arg.Any<CancellationToken>());

        cut.Render(p => p.Add(x => x.Text, "ab"));
        Clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Render(p => p.Add(x => x.Text, "abc"));
        Clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Render(p => p.Add(x => x.Text, "abcd"));
        Clock.Advance(TimeSpan.FromMilliseconds(499));
        Queries.ReceivedWithAnyArgs(1).PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>());

        Clock.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Queries.ReceivedWithAnyArgs(2).PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>()));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "abcd", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SetText_ChangeWhileACallRuns_CancelsThatCallAndShowsOnlyTheLaterPreview()
    {
        var running = new TaskCompletionSource<ErrorOr<IReadOnlyList<TemplatePreview>>>();
        CancellationToken first = default;
        Queries.PreviewTemplateAsync(Lookup(), "a", Arg.Do<CancellationToken>(ct => first = ct)).Returns(new ValueTask<ErrorOr<IReadOnlyList<TemplatePreview>>>(running.Task));
        Queries.PreviewTemplateAsync(Lookup(), "ab", Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<TemplatePreview>>([new TemplatePreview("Email:Requests", "Later")]));
        var cut = RenderPane(Lookup(), "a");

        cut.Render(p => p.Add(x => x.Text, "ab"));
        Clock.Advance(TimeSpan.FromMilliseconds(500));
        cut.WaitForAssertion(() => Assert.Equal("Later", cut.Find("pre").TextContent));
        running.SetResult(ErrorOr<IReadOnlyList<TemplatePreview>>.ForValue([new TemplatePreview("Email:Requests", "Earlier")]));

        Assert.Equal((true, "Later"), (first.IsCancellationRequested, cut.Find("pre").TextContent));
    }

    private void GivenPreviews(params TemplatePreview[] previews) =>
        Queries.PreviewTemplateAsync(default, default!, default).ReturnsForAnyArgs(Ok<IReadOnlyList<TemplatePreview>>(previews));

    private IRenderedComponent<TemplatePreviewPane> RenderPane(TemplateLookup lookup, string text, bool hasSample = true) =>
        Render<TemplatePreviewPane>(p => p.Add(x => x.Lookup, lookup).Add(x => x.Text, text).Add(x => x.HasSample, hasSample));
}
