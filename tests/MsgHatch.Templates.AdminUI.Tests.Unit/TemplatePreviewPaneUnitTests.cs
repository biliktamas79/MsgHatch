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
    private const string _text = "Text";

    private static readonly TemplateLookup _body = Lookup(part: "body", kind: TemplatePartKind.Html);

    [Fact]
    public void Render_HtmlPart_ShowsThePreviewInAnIframeWithAnEmptySandboxAndTheTextAsSrcdoc()
    {
        // Arrange
        const string text = "<p>Hello {{Name}}</p>";
        const string previewText = "<p>Hello <b>Sample</b></p><script>alert(1)</script>";
        GivenPreviews(new TemplatePreview("Email:Requests", previewText));

        // Act
        var cut = RenderPane(_body, text);

        // Assert
        var frame = cut.Find("iframe[data-preview=html]");
        Assert.Equal(
            ("", previewText),
            (frame.GetAttribute("sandbox"), frame.GetAttribute("srcdoc")));
        Assert.Empty(cut.FindAll("pre"));
        Queries.Received(1).PreviewTemplateAsync(_body, text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_TextPart_ShowsThePreviewInAPre()
    {
        // Arrange
        const string text = "Hello {{Name}}";
        const string previewText = "Hello <Sample>";
        GivenPreviews(new TemplatePreview("Email:Requests", previewText));

        // Act
        var cut = RenderPane(Lookup(), text);

        // Assert
        Assert.Equal(previewText, cut.Find("pre[data-preview=text]").TextContent);
        Assert.Empty(cut.FindAll("iframe"));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Change_RegistrationOfTwo_ShowsThePreviewOfTheSelectedRegistration()
    {
        // Arrange
        const string email = "Email:Requests";
        const string push = "Push:Requests";
        const string emailPreview = "From the email";
        const string pushPreview = "From the push";
        GivenPreviews(new TemplatePreview(email, emailPreview), new TemplatePreview(push, pushPreview));
        var cut = RenderPane(Lookup(), _text);
        var first = cut.Find("pre").TextContent;

        // Act
        cut.Find("select[data-field=registration]").Change(push);

        // Assert
        Assert.Equal(
            ($"{email}, {push}", emailPreview, pushPreview),
            (string.Join(", ", cut.FindAll("select option").Select(static option => option.TextContent)), first, cut.Find("pre").TextContent));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), _text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_OnePreview_ShowsNoSelect()
    {
        // Arrange
        GivenPreviews(new TemplatePreview("Email:Requests", "Hello"));

        // Act
        var cut = RenderPane(Lookup(), _text);

        // Assert
        Assert.Empty(cut.FindAll("select"));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), _text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_PartWithNoSample_ShowsNoSampleDataAndCallsNothing()
    {
        // Act
        var cut = RenderPane(Lookup(), _text, hasSample: false);

        // Assert
        Assert.Equal("No sample data", cut.Find("[data-state=no-sample]").TextContent);
        Assert.Empty(Queries.ReceivedCalls());
    }

    [Fact]
    public void Render_NoPreview_ShowsNoSampleData()
    {
        // Act
        var cut = RenderPane(Lookup(), _text);

        // Assert
        Assert.Equal("No sample data", cut.Find("[data-state=no-sample]").TextContent);
        Queries.Received(1).PreviewTemplateAsync(Lookup(), _text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_TemplateInvalid_ShowsItsMessageInPlaceOfThePreview()
    {
        // Arrange
        const string invalidText = "Hello {{Name";
        const string message = "The template has an unclosed placeholder.";
        Queries.PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(Fail<IReadOnlyList<TemplatePreview>>(Errors.TemplateInvalid, message));

        // Act
        var cut = RenderPane(Lookup(), invalidText);

        // Assert
        Assert.Equal(message, cut.Find(".alert-danger").TextContent);
        Assert.Empty(cut.FindAll("[data-preview]"));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), invalidText, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        // Arrange
        Queries.PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Fail<IReadOnlyList<TemplatePreview>>(Errors.Unavailable));

        // Act
        var cut = RenderPane(Lookup(), _text);

        // Assert
        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
        Queries.Received(1).PreviewTemplateAsync(Lookup(), _text, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SetText_ThreeChangesAndAPause_CallsThePreviewOneTimeForTheFirstTextAndOneTimeForTheLastText()
    {
        // Arrange
        const string firstText = "a";
        const string secondText = "ab";
        const string thirdText = "abc";
        const string lastText = "abcd";
        var cut = RenderPane(Lookup(), firstText);
        Queries.Received(1).PreviewTemplateAsync(Lookup(), firstText, Arg.Any<CancellationToken>());

        // Act
        cut.Render(p => p.Add(x => x.Text, secondText));
        Clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Render(p => p.Add(x => x.Text, thirdText));
        Clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Render(p => p.Add(x => x.Text, lastText));
        Clock.Advance(TimeSpan.FromMilliseconds(499));
        Queries.DidNotReceive().PreviewTemplateAsync(Lookup(), Arg.Is<string>(static text => text != firstText), Arg.Any<CancellationToken>());

        Clock.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        cut.WaitForAssertion(() => Queries.Received(1).PreviewTemplateAsync(Lookup(), lastText, Arg.Any<CancellationToken>()));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), firstText, Arg.Any<CancellationToken>());
        Queries.DidNotReceive().PreviewTemplateAsync(Lookup(), secondText, Arg.Any<CancellationToken>());
        Queries.DidNotReceive().PreviewTemplateAsync(Lookup(), thirdText, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SetText_ChangeWhileACallRuns_CancelsThatCallAndShowsOnlyTheLaterPreview()
    {
        // Arrange
        const string firstText = "a";
        const string laterText = "ab";
        const string laterPreview = "Later";
        var running = new TaskCompletionSource<ErrorOr<IReadOnlyList<TemplatePreview>>>();
        CancellationToken first = default;
        Queries.PreviewTemplateAsync(Lookup(), firstText, Arg.Do<CancellationToken>(ct => first = ct)).Returns(new ValueTask<ErrorOr<IReadOnlyList<TemplatePreview>>>(running.Task));
        Queries.PreviewTemplateAsync(Lookup(), laterText, Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<TemplatePreview>>([new TemplatePreview("Email:Requests", laterPreview)]));
        var cut = RenderPane(Lookup(), firstText);

        // Act
        cut.Render(p => p.Add(x => x.Text, laterText));
        Clock.Advance(TimeSpan.FromMilliseconds(500));
        cut.WaitForAssertion(() => Assert.Equal(laterPreview, cut.Find("pre").TextContent));
        running.SetResult(ErrorOr<IReadOnlyList<TemplatePreview>>.ForValue([new TemplatePreview("Email:Requests", "Earlier")]));

        // Assert
        Assert.Equal((true, laterPreview), (first.IsCancellationRequested, cut.Find("pre").TextContent));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), firstText, Arg.Any<CancellationToken>());
        Queries.Received(1).PreviewTemplateAsync(Lookup(), laterText, Arg.Any<CancellationToken>());
    }

    private void GivenPreviews(params TemplatePreview[] previews) =>
        Queries.PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok<IReadOnlyList<TemplatePreview>>(previews));

    private IRenderedComponent<TemplatePreviewPane> RenderPane(TemplateLookup lookup, string text, bool hasSample = true) =>
        Render<TemplatePreviewPane>(p => p.Add(x => x.Lookup, lookup).Add(x => x.Text, text).Add(x => x.HasSample, hasSample));
}
