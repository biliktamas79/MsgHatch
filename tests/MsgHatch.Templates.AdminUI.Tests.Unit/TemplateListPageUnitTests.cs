using Bunit;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Templates.Management;
using NSubstitute;
using static MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes.TemplateTestData;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class TemplateListPageUnitTests : TemplatePageContext
{
    public TemplateListPageUnitTests()
    {
        Queries.GetTemplatePartsAsync(null, Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<TemplatePartInfo>>([Part(), Part("body", TemplatePartKind.Html)]));
    }

    [Fact]
    public void Render_TwoTemplates_ShowsARowForEachTemplateWithTheButtonsViewAndEdit()
    {
        var company = Lookup("hu", "42", "body", TemplatePartKind.Html);
        GivenTemplates(new TemplateEntry(Lookup(), 7), new TemplateEntry(company, 3));

        var cut = Render<TemplateListPage>();

        var rows = cut.FindAll("tr[data-row]");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["Requests", "en", "All companies", "subject", "PlainText", "7"], rows[0].Children.SkipLast(1).Select(static cell => cell.TextContent));
        Assert.Equal(["View", "Edit"], rows[0].Children[^1].Children.Select(static button => button.TextContent));
        Assert.Equal(
            (TemplateAdminRoutes.View(company).TrimStart('/'), TemplateAdminRoutes.Edit(company).TrimStart('/')),
            (rows[1].QuerySelector("[data-action=view]")!.GetAttribute("href"), rows[1].QuerySelector("[data-action=edit]")!.GetAttribute("href")));
        Assert.Equal("msghatch/templates/Requests/hu/body/Html/view?companyId=42", rows[1].QuerySelector("[data-action=view]")!.GetAttribute("href"));
    }

    [Fact]
    public void Render_Page_HasTheListLayoutWithTheFiltersInTheToolbar()
    {
        GivenTemplates(new TemplateEntry(Lookup(), 7));

        var cut = Render<TemplateListPage>();

        Assert.Equal(
            ("Templates", 1, 1, true),
            (cut.Find("[data-list-header] h1").TextContent, cut.FindAll("[data-list-filters] select").Count, cut.FindAll("[data-list-filters] input").Count,
                cut.Find("[data-list-body]").QuerySelector("[data-list-scroll] table") is not null));
    }

    [Fact]
    public void Render_NoTemplate_ShowsTheEmptyState()
    {
        GivenTemplates();

        var cut = Render<TemplateListPage>();

        Assert.Equal("There are no templates.", cut.Find("[data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        Queries.GetTemplatesAsync(Arg.Any<TemplateQuery>(), Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<TemplateEntry>>(Errors.Unavailable, "Connection refused."));

        var cut = Render<TemplateListPage>();

        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        Queries.GetTemplatesAsync(Arg.Any<TemplateQuery>(), Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<TemplateEntry>>(Errors.Rejected));

        var cut = Render<TemplateListPage>();

        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_DeclaredParts_OffersEachMessageTypeOneTimeAsAFilter()
    {
        GivenTemplates();

        var cut = Render<TemplateListPage>();

        Assert.Equal(["", "Requests"], cut.FindAll("#msghatch-template-type option").Select(static option => option.GetAttribute("value")));
    }

    [Fact]
    public void ClickShow_MessageTypeAndCompany_QueriesTheTemplatesWithBothFilters()
    {
        GivenTemplates();
        var cut = Render<TemplateListPage>();

        cut.Find("#msghatch-template-type").Change("Requests");
        cut.Find("#msghatch-template-company").Change(" 42 ");
        cut.Find("[data-action=filter]").Click();

        Queries.Received(1).GetTemplatesAsync(new TemplateQuery(Requests, "42"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickShow_MessageTypeThatIsNotAName_ShowsValidationFailedAndQueriesNothing()
    {
        GivenTemplates();
        var cut = Render<TemplateListPage>();
        Queries.ClearReceivedCalls();

        cut.Find("#msghatch-template-type").Change("not a name");
        cut.Find("[data-action=filter]").Click();

        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Queries.DidNotReceiveWithAnyArgs().GetTemplatesAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickOpenTheEditor_DeclaredPart_NavigatesToTheEditPageOfTheLookup()
    {
        GivenTemplates();
        var cut = Render<TemplateListPage>();

        cut.Find("#msghatch-template-part").Change("1");
        cut.Find("#msghatch-template-language").Change("hu");
        cut.Find("#msghatch-template-new-company").Change("42");
        cut.Find("[data-action=new]").Click();

        Assert.Equal("http://localhost/msghatch/templates/Requests/hu/body/Html?companyId=42", Navigation.Uri);
    }

    [Fact]
    public void ClickOpenTheEditor_EmptyLanguage_ShowsValidationFailedAndStays()
    {
        GivenTemplates();
        var cut = Render<TemplateListPage>();

        cut.Find("#msghatch-template-language").Change(" ");
        cut.Find("[data-action=new]").Click();

        Assert.Equal("400", cut.Find("[data-form=new] ~ .alert-danger").GetAttribute("data-error-code"));
        Assert.Equal("http://localhost/", Navigation.Uri);
    }

    [Fact]
    public void Render_PartsUnavailable_ShowsTheTextOfUnavailableForTheNewTemplate()
    {
        GivenTemplates();
        Queries.GetTemplatePartsAsync(null, Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<TemplatePartInfo>>(Errors.Unavailable));

        var cut = Render<TemplateListPage>();

        Assert.Equal(AdminErrors.UnavailableText, cut.Find("h2 + .alert-danger").TextContent);
    }

    [Fact]
    public void Render_NoDeclaredPart_ShowsThatNoMessageTypeDeclaresAPart()
    {
        GivenTemplates();
        Queries.GetTemplatePartsAsync(null, Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<TemplatePartInfo>>([]));

        var cut = Render<TemplateListPage>();

        Assert.Single(cut.FindAll("[data-state=no-parts]"));
    }

    private void GivenTemplates(params TemplateEntry[] entries) =>
        Queries.GetTemplatesAsync(Arg.Any<TemplateQuery>(), Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<TemplateEntry>>(entries));
}
