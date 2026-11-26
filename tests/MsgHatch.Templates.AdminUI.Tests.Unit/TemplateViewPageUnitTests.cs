using Bunit;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Templates.Management;
using NSubstitute;
using static MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes.TemplateTestData;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class TemplateViewPageUnitTests : TemplatePageContext
{
    public TemplateViewPageUnitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Queries.GetTemplatePartsAsync(Requests, Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<TemplatePartInfo>>([Part(), Part("body", TemplatePartKind.Html)]));
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Ok(Source("Hello {{Name}}", 7)));
    }

    [Fact]
    public void Render_StoredTemplateWithASample_ShowsThePreviewOfTheStoredTextNextToTheText()
    {
        // Arrange
        Queries.PreviewTemplateAsync(Lookup(), "Hello {{Name}}", Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<TemplatePreview>>([new TemplatePreview("Email:Requests", "Hello Sample")]));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal("Hello Sample", cut.Find("[data-column=preview] pre[data-preview=text]").TextContent);
        Assert.Equal(("col-lg-6", "col-lg-6"), (cut.Find("[data-column=text]").ClassName, cut.Find("[data-column=preview]").ClassName));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "Hello {{Name}}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_PartWithNoSample_ShowsNoSampleDataAndCallsNoPreview()
    {
        // Arrange
        Queries.GetTemplatePartsAsync(Requests, Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<TemplatePartInfo>>([Part() with { HasSample = false }]));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Single(cut.FindAll("[data-column=preview] [data-state=no-sample]"));
        Queries.DidNotReceiveWithAnyArgs().PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_PartsRejected_ShowsTheTextOfRejectedAndReadsNoTemplate()
    {
        // Arrange
        Queries.GetTemplatePartsAsync(Requests, Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<TemplatePartInfo>>(Errors.Rejected));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
        Queries.DidNotReceiveWithAnyArgs().GetTemplateAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_StoredHtmlTemplate_ShowsTheMetadataAndTheTextReadOnlyWithTheHtmlHighlight()
    {
        // Arrange
        var lookup = Lookup(part: "body", kind: TemplatePartKind.Html);
        Queries.GetTemplateAsync(lookup, Arg.Any<CancellationToken>()).Returns(Ok(Source("<p>Hello {{Name}}</p>", 3)));

        // Act
        var cut = RenderPage("body", "Html");

        // Assert
        var text = cut.Find("[data-view=text] textarea");
        Assert.Equal(("<p>Hello {{Name}}</p>", true), (text.GetAttribute("value"), text.HasAttribute("readonly")));
        Assert.Equal(["p", "p"], cut.FindAll("[data-view=text] pre span.html-tagname").Select(static span => span.TextContent));
        Assert.Equal("3", cut.Find("[data-metadata] [data-field=version]").TextContent);
    }

    [Fact]
    public void Render_StoredTemplate_PutsTheMetadataInTheHeaderAndTheActionsUnderTheScrollingBody()
    {
        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(
            (true, true, true),
            (cut.Find("[data-edit-header]").QuerySelector("[data-metadata]") is not null, cut.Find("[data-edit-body]").QuerySelector("[data-view=text]") is not null,
                cut.Find("[data-edit-actions]").QuerySelector("[data-action=edit]") is not null));
    }

    [Fact]
    public void Render_StoredTextTemplate_ShowsTheTextReadOnlyInOneSegment()
    {
        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(("Hello {{Name}}", true), (cut.Find("[data-view=text] textarea").GetAttribute("value"), cut.Find("[data-view=text] textarea").HasAttribute("readonly")));
        Assert.Equal(["Hello {{Name}}"], cut.FindAll("[data-view=text] pre span").Select(static span => span.TextContent));
    }

    [Fact]
    public void Render_TemplateOfACompany_HasTheButtonsEditAndBackWithTheAddressesOfTheRouteBuilders()
    {
        // Arrange
        Queries.GetTemplateAsync(Lookup(scope: "42"), Arg.Any<CancellationToken>()).Returns(Ok(Source("For 42", 2)));
        Navigation.NavigateTo("msghatch/templates/Requests/en/subject/PlainText/view?companyId=42");

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(
            (TemplateAdminRoutes.Edit(Lookup(scope: "42")).TrimStart('/'), TemplateAdminRoutes.List.TrimStart('/')),
            (cut.Find("[data-actions-end] [data-action=edit]").GetAttribute("href"), cut.Find("[data-actions-start] [data-action=back]").GetAttribute("href")));
        Assert.Equal("For 42", cut.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void Render_MissingTemplate_ShowsTheErrorAndTheLinkBackToTheList()
    {
        // Arrange
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Fail<TemplateSource>(Errors.NotFound, "No template."));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal("404", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Assert.Equal(TemplateAdminRoutes.List.TrimStart('/'), cut.Find("a").GetAttribute("href"));
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        // Arrange
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Fail<TemplateSource>(Errors.Unavailable));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    [Theory]
    [InlineData("1Requests", "en", "subject", "PlainText")]
    [InlineData("Requests", "en", "sub ject", "PlainText")]
    [InlineData("Requests", "en", "subject", "Markdown")]
    public void Render_InvalidAddress_ShowsValidationFailedAndCallsNothing(string type, string language, string part, string kind)
    {
        // Act
        var cut = Render<TemplateViewPage>(p => p.Add(x => x.TypeName, type).Add(x => x.Language, language).Add(x => x.Part, part).Add(x => x.KindName, kind));

        // Assert
        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Assert.Empty(Queries.ReceivedCalls());
    }

    private IRenderedComponent<TemplateViewPage> RenderPage(string part = "subject", string kind = "PlainText") =>
        Render<TemplateViewPage>(p => p.Add(x => x.TypeName, "Requests").Add(x => x.Language, "en").Add(x => x.Part, part).Add(x => x.KindName, kind));
}
