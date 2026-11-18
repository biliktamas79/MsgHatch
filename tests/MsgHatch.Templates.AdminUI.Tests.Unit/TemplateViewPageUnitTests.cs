using Bunit;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes.TemplateTestData;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class TemplateViewPageUnitTests : TemplatePageContext
{
    public TemplateViewPageUnitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Ok(Source("Hello {{Name}}", 7)));
    }

    [Fact]
    public void Render_StoredHtmlTemplate_ShowsTheMetadataAndTheTextReadOnlyWithTheHtmlHighlight()
    {
        var lookup = Lookup(part: "body", kind: TemplatePartKind.Html);
        Queries.GetTemplateAsync(lookup, Arg.Any<CancellationToken>()).Returns(Ok(Source("<p>Hello {{Name}}</p>", 3)));

        var cut = RenderPage("body", "Html");

        var text = cut.Find("[data-view=text] textarea");
        Assert.Equal(("<p>Hello {{Name}}</p>", true), (text.GetAttribute("value"), text.HasAttribute("readonly")));
        Assert.Equal(["p", "p"], cut.FindAll("[data-view=text] pre span.html-tagname").Select(static span => span.TextContent));
        Assert.Equal("3", cut.Find("[data-metadata] [data-field=version]").TextContent);
    }

    [Fact]
    public void Render_StoredTextTemplate_ShowsTheTextReadOnlyInOneSegment()
    {
        var cut = RenderPage();

        Assert.Equal(("Hello {{Name}}", true), (cut.Find("[data-view=text] textarea").GetAttribute("value"), cut.Find("[data-view=text] textarea").HasAttribute("readonly")));
        Assert.Equal(["Hello {{Name}}"], cut.FindAll("[data-view=text] pre span").Select(static span => span.TextContent));
    }

    [Fact]
    public void Render_TemplateOfACompany_HasTheButtonsEditAndBackWithTheAddressesOfTheRouteBuilders()
    {
        Queries.GetTemplateAsync(Lookup(scope: "42"), Arg.Any<CancellationToken>()).Returns(Ok(Source("For 42", 2)));
        Navigation.NavigateTo("msghatch/templates/Requests/en/subject/PlainText/view?companyId=42");

        var cut = RenderPage();

        Assert.Equal(
            (TemplateAdminRoutes.Edit(Lookup(scope: "42")).TrimStart('/'), TemplateAdminRoutes.List.TrimStart('/')),
            (cut.Find("[data-action=edit]").GetAttribute("href"), cut.Find("[data-action=back]").GetAttribute("href")));
        Assert.Equal("For 42", cut.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void Render_MissingTemplate_ShowsTheErrorAndTheLinkBackToTheList()
    {
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Fail<TemplateSource>(Errors.NotFound, "No template."));

        var cut = RenderPage();

        Assert.Equal("404", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Assert.Equal(TemplateAdminRoutes.List.TrimStart('/'), cut.Find("a").GetAttribute("href"));
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Fail<TemplateSource>(Errors.Unavailable));

        var cut = RenderPage();

        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    [Theory]
    [InlineData("1Requests", "en", "subject", "PlainText")]
    [InlineData("Requests", "en", "sub ject", "PlainText")]
    [InlineData("Requests", "en", "subject", "Markdown")]
    public void Render_InvalidAddress_ShowsValidationFailedAndCallsNothing(string type, string language, string part, string kind)
    {
        var cut = Render<TemplateViewPage>(p => p.Add(x => x.TypeName, type).Add(x => x.Language, language).Add(x => x.Part, part).Add(x => x.KindName, kind));

        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Assert.Empty(Queries.ReceivedCalls());
    }

    private IRenderedComponent<TemplateViewPage> RenderPage(string part = "subject", string kind = "PlainText") =>
        Render<TemplateViewPage>(p => p.Add(x => x.TypeName, "Requests").Add(x => x.Language, "en").Add(x => x.Part, part).Add(x => x.KindName, kind));
}
