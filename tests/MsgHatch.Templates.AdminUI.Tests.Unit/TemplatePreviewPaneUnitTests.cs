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
        // Arrange
        GivenPreviews(new TemplatePreview("Email:Requests", "<p>Hello <b>Sample</b></p><script>alert(1)</script>"));

        // Act
        var cut = RenderPane(_body, "<p>Hello {{Name}}</p>");

        // Assert
        var frame = cut.Find("iframe[data-preview=html]");
        Assert.Equal(
            ("", "<p>Hello <b>Sample</b></p><script>alert(1)</script>"),
            (frame.GetAttribute("sandbox"), frame.GetAttribute("srcdoc")));
        Assert.Empty(cut.FindAll("pre"));
        Queries.Received(1).PreviewTemplateAsync(_body, "<p>Hello {{Name}}</p>", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_TextPart_ShowsThePreviewInAPre()
    {
        // Arrange
        GivenPreviews(new TemplatePreview("Email:Requests", "Hello <Sample>"));

        // Act
        var cut = RenderPane(Lookup(), "Hello {{Name}}");

        // Assert
        Assert.Equal("Hello <Sample>", cut.Find("pre[data-preview=text]").TextContent);
        Assert.Empty(cut.FindAll("iframe"));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "Hello {{Name}}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Change_RegistrationOfTwo_ShowsThePreviewOfTheSelectedRegistration()
    {
        // Arrange
        GivenPreviews(new TemplatePreview("Email:Requests", "From the email"), new TemplatePreview("Push:Requests", "From the push"));
        var cut = RenderPane(Lookup(), "Text");
        var first = cut.Find("pre").TextContent;

        // Act
        cut.Find("select[data-field=registration]").Change("Push:Requests");

        // Assert
        Assert.Equal(
            ("Email:Requests, Push:Requests", "From the email", "From the push"),
            (string.Join(", ", cut.FindAll("select option").Select(static option => option.TextContent)), first, cut.Find("pre").TextContent));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "Text", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_OnePreview_ShowsNoSelect()
    {
        // Arrange
        GivenPreviews(new TemplatePreview("Email:Requests", "Hello"));

        // Act
        var cut = RenderPane(Lookup(), "Text");

        // Assert
        Assert.Empty(cut.FindAll("select"));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "Text", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_PartWithNoSample_ShowsNoSampleDataAndCallsNothing()
    {
        // Act
        var cut = RenderPane(Lookup(), "Text", hasSample: false);

        // Assert
        Assert.Equal("No sample data", cut.Find("[data-state=no-sample]").TextContent);
        Assert.Empty(Queries.ReceivedCalls());
    }

    [Fact]
    public void Render_NoPreview_ShowsNoSampleData()
    {
        // Act
        var cut = RenderPane(Lookup(), "Text");

        // Assert
        Assert.Equal("No sample data", cut.Find("[data-state=no-sample]").TextContent);
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "Text", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_TemplateInvalid_ShowsItsMessageInPlaceOfThePreview()
    {
        // Arrange
        Queries.PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(Fail<IReadOnlyList<TemplatePreview>>(Errors.TemplateInvalid, "The template has an unclosed placeholder."));

        // Act
        var cut = RenderPane(Lookup(), "Hello {{Name");

        // Assert
        Assert.Equal("The template has an unclosed placeholder.", cut.Find(".alert-danger").TextContent);
        Assert.Empty(cut.FindAll("[data-preview]"));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "Hello {{Name", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        // Arrange
        Queries.PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<IReadOnlyList<TemplatePreview>>(Errors.Unavailable));

        // Act
        var cut = RenderPane(Lookup(), "Text");

        // Assert
        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "Text", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SetText_ThreeChangesAndAPause_CallsThePreviewOneTimeForTheFirstTextAndOneTimeForTheLastText()
    {
        // Arrange
        var cut = RenderPane(Lookup(), "a");
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "a", Arg.Any<CancellationToken>());

        // Act
        cut.Render(p => p.Add(x => x.Text, "ab"));
        Clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Render(p => p.Add(x => x.Text, "abc"));
        Clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Render(p => p.Add(x => x.Text, "abcd"));
        Clock.Advance(TimeSpan.FromMilliseconds(499));
        Queries.DidNotReceive().PreviewTemplateAsync(Lookup(), Arg.Is<string>(static text => text != "a"), Arg.Any<CancellationToken>());

        Clock.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        cut.WaitForAssertion(() => Queries.Received(1).PreviewTemplateAsync(Lookup(), "abcd", Arg.Any<CancellationToken>()));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "a", Arg.Any<CancellationToken>());
        Queries.DidNotReceive().PreviewTemplateAsync(Lookup(), "ab", Arg.Any<CancellationToken>());
        Queries.DidNotReceive().PreviewTemplateAsync(Lookup(), "abc", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SetText_ChangeWhileACallRuns_CancelsThatCallAndShowsOnlyTheLaterPreview()
    {
        // Arrange
        var running = new TaskCompletionSource<ErrorOr<IReadOnlyList<TemplatePreview>>>();
        CancellationToken first = default;
        Queries.PreviewTemplateAsync(Lookup(), "a", Arg.Do<CancellationToken>(ct => first = ct)).Returns(new ValueTask<ErrorOr<IReadOnlyList<TemplatePreview>>>(running.Task));
        Queries.PreviewTemplateAsync(Lookup(), "ab", Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<TemplatePreview>>([new TemplatePreview("Email:Requests", "Later")]));
        var cut = RenderPane(Lookup(), "a");

        // Act
        cut.Render(p => p.Add(x => x.Text, "ab"));
        Clock.Advance(TimeSpan.FromMilliseconds(500));
        cut.WaitForAssertion(() => Assert.Equal("Later", cut.Find("pre").TextContent));
        running.SetResult(ErrorOr<IReadOnlyList<TemplatePreview>>.ForValue([new TemplatePreview("Email:Requests", "Earlier")]));

        // Assert
        Assert.Equal((true, "Later"), (first.IsCancellationRequested, cut.Find("pre").TextContent));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "a", Arg.Any<CancellationToken>());
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "ab", Arg.Any<CancellationToken>());
    }

    private void GivenPreviews(params TemplatePreview[] previews) =>
        Queries.PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok<IReadOnlyList<TemplatePreview>>(previews));

    private IRenderedComponent<TemplatePreviewPane> RenderPane(TemplateLookup lookup, string text, bool hasSample = true) =>
        Render<TemplatePreviewPane>(p => p.Add(x => x.Lookup, lookup).Add(x => x.Text, text).Add(x => x.HasSample, hasSample));
}
