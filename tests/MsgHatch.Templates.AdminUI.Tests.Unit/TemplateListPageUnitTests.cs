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
        // Arrange
        var company = Lookup("hu", "42", "body", TemplatePartKind.Html);
        GivenTemplates(new TemplateEntry(Lookup(), 7), new TemplateEntry(company, 3));

        // Act
        var cut = Render<TemplateListPage>();

        // Assert
        var rows = cut.FindAll("tr[data-row]");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["Requests", "en", "All companies", "subject", "PlainText", "7"], rows[0].Children.SkipLast(1).Select(static cell => cell.TextContent));
        Assert.Equal(["View", "Edit", "Delete"], rows[0].Children[^1].Children.Select(static button => button.TextContent));
        Assert.Equal(
            (TemplateAdminRoutes.View(company).TrimStart('/'), TemplateAdminRoutes.Edit(company).TrimStart('/')),
            (rows[1].QuerySelector("[data-action=view]")!.GetAttribute("href"), rows[1].QuerySelector("[data-action=edit]")!.GetAttribute("href")));
        Assert.Equal("msghatch/templates/Requests/hu/body/Html/view?companyId=42", rows[1].QuerySelector("[data-action=view]")!.GetAttribute("href"));
    }

    [Fact]
    public void Render_Page_HasTheListLayoutWithTheFiltersInTheToolbar()
    {
        // Arrange
        GivenTemplates(new TemplateEntry(Lookup(), 7));

        // Act
        var cut = Render<TemplateListPage>();

        // Assert
        Assert.Equal(
            ("Templates", 3, 2, true),
            (cut.Find("[data-list-header] h1").TextContent, cut.FindAll("[data-list-filters] select").Count, cut.FindAll("[data-list-filters] input").Count,
                cut.Find("[data-list-body]").QuerySelector("[data-list-scroll] table") is not null));
    }

    [Fact]
    public void Render_EntryWithAVersionText_ShowsTheTextOfTheStoreInTheVersionColumnAndTheDeleteQuestion()
    {
        // Arrange
        GivenTemplates(new TemplateEntry(Lookup(), 7) { VersionText = "Built in" });

        // Act
        var cut = Render<TemplateListPage>();
        cut.Find("tr[data-row] [data-action=delete]").Click();

        // Assert
        Assert.Equal(
            ("Built in", "Built in"),
            (cut.Find("tr[data-row]").Children[5].TextContent, cut.Find(".modal [data-metadata] [data-field=version]").TextContent));
    }

    [Fact]
    public void Render_NoTemplate_ShowsTheEmptyState()
    {
        // Arrange
        GivenTemplates();

        // Act
        var cut = Render<TemplateListPage>();

        // Assert
        Assert.Equal("There are no templates.", cut.Find("[data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        // Arrange
        Queries.GetTemplatesAsync(Arg.Any<TemplateQuery>(), Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<TemplateEntry>>(Errors.Unavailable, "Connection refused."));

        // Act
        var cut = Render<TemplateListPage>();

        // Assert
        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        // Arrange
        Queries.GetTemplatesAsync(Arg.Any<TemplateQuery>(), Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<TemplateEntry>>(Errors.Rejected));

        // Act
        var cut = Render<TemplateListPage>();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_DeclaredParts_OffersEachMessageTypeOneTimeAsAFilter()
    {
        // Arrange
        GivenTemplates();

        // Act
        var cut = Render<TemplateListPage>();

        // Assert
        Assert.Equal(["", "Requests"], cut.FindAll("#msghatch-template-type option").Select(static option => option.GetAttribute("value")));
    }

    [Fact]
    public void Render_DeclaredParts_OffersEachPartOneTimeAndEachKindAsAFilter()
    {
        // Arrange
        GivenTemplates();

        // Act
        var cut = Render<TemplateListPage>();

        // Assert
        Assert.Equal(["", "subject", "body"], cut.FindAll("#msghatch-template-part-filter option").Select(static option => option.GetAttribute("value")));
        Assert.Equal(["", "PlainText", "Html"], cut.FindAll("#msghatch-template-kind option").Select(static option => option.GetAttribute("value")));
    }

    [Fact]
    public void Change_LanguagePartAndKind_ShowsOnlyTheTemplatesThatMatchAllThree()
    {
        // Arrange
        GivenTemplates(
            new TemplateEntry(Lookup("en", null, "body", TemplatePartKind.Html), 1),
            new TemplateEntry(Lookup("hu", null, "body", TemplatePartKind.Html), 2),
            new TemplateEntry(Lookup("en", null, "subject", TemplatePartKind.PlainText), 3),
            new TemplateEntry(Lookup("en", null, "body", TemplatePartKind.PlainText), 4));
        var cut = Render<TemplateListPage>();

        // Act
        cut.Find("#msghatch-template-language-filter").Input(" EN ");
        cut.Find("#msghatch-template-part-filter").Change("body");
        cut.Find("#msghatch-template-kind").Change("Html");

        // Assert
        var row = Assert.Single(cut.FindAll("tr[data-row]"));
        Assert.Equal(["Requests", "en", "All companies", "body", "Html", "1"], row.Children.SkipLast(1).Select(static cell => cell.TextContent));
    }

    [Fact]
    public void Change_MessageTypeAndCompany_QueriesTheTemplatesWithBothFilters()
    {
        // Arrange
        GivenTemplates();
        var cut = Render<TemplateListPage>();

        // Act
        cut.Find("#msghatch-template-company").Input(" 42 ");
        cut.Find("#msghatch-template-type").Change("Requests");

        // Assert
        Queries.Received(1).GetTemplatesAsync(new TemplateQuery(Requests, "42"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Input_CompanyTwiceWithinThePause_QueriesOnlyTheLastTextAfter500Milliseconds()
    {
        // Arrange
        GivenTemplates();
        var cut = Render<TemplateListPage>();

        // Act
        cut.Find("#msghatch-template-company").Input("4");
        Clock.Advance(TimeSpan.FromMilliseconds(499));
        cut.Find("#msghatch-template-company").Input("42");
        Clock.Advance(TimeSpan.FromMilliseconds(500));

        // Assert
        cut.WaitForAssertion(() => Queries.Received(1).GetTemplatesAsync(new TemplateQuery(null, "42"), Arg.Any<CancellationToken>()));
        Queries.DidNotReceive().GetTemplatesAsync(new TemplateQuery(null, "4"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Input_Language_FiltersTheListOnlyAfter500Milliseconds()
    {
        // Arrange
        GivenTemplates(new TemplateEntry(Lookup("en"), 1), new TemplateEntry(Lookup("hu"), 2));
        var cut = Render<TemplateListPage>();

        // Act
        cut.Find("#msghatch-template-language-filter").Input("hu");
        Clock.Advance(TimeSpan.FromMilliseconds(499));
        var beforeThePause = await cut.InvokeAsync(() => LanguagesOf(cut));
        Clock.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        cut.WaitForAssertion(() => Assert.Equal(("en hu", "hu"), (beforeThePause, LanguagesOf(cut))));
    }

    [Fact]
    public async Task Input_CompanyWhileAnEarlierQueryRuns_ShowsOnlyTheAnswerOfTheLastQuery()
    {
        // Arrange
        var earlier = new TaskCompletionSource<ErrorOr<IReadOnlyList<TemplateEntry>>>();
        GivenTemplates();
        Queries.GetTemplatesAsync(new TemplateQuery(null, "4"), Arg.Any<CancellationToken>()).Returns(new ValueTask<ErrorOr<IReadOnlyList<TemplateEntry>>>(earlier.Task));
        Queries.GetTemplatesAsync(new TemplateQuery(null, "42"), Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<TemplateEntry>>([new TemplateEntry(Lookup(scope: "42"), 2)]));
        var cut = Render<TemplateListPage>();

        // Act
        cut.Find("#msghatch-template-company").Input("4");
        Clock.Advance(TimeSpan.FromMilliseconds(500));
        cut.WaitForAssertion(() => Queries.Received(1).GetTemplatesAsync(new TemplateQuery(null, "4"), Arg.Any<CancellationToken>()));
        cut.Find("#msghatch-template-company").Input("42");
        Clock.Advance(TimeSpan.FromMilliseconds(500));
        cut.WaitForAssertion(() => Assert.Equal(["42"], cut.FindAll("tr[data-row]").Select(static row => row.Children[2].TextContent)));
        await cut.InvokeAsync(() => earlier.SetResult(ErrorOr<IReadOnlyList<TemplateEntry>>.ForValue([new TemplateEntry(Lookup(scope: "4"), 1)])));

        // Assert
        Assert.Equal(["42"], cut.FindAll("tr[data-row]").Select(static row => row.Children[2].TextContent));
    }

    [Fact]
    public void Change_MessageTypeThatIsNotAName_ShowsValidationFailedAndQueriesNothing()
    {
        // Arrange
        GivenTemplates();
        var cut = Render<TemplateListPage>();
        Queries.ClearReceivedCalls();

        // Act
        cut.Find("#msghatch-template-type").Change("not a name");

        // Assert
        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Queries.DidNotReceiveWithAnyArgs().GetTemplatesAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickOpenTheEditor_DeclaredPart_NavigatesToTheEditPageOfTheLookup()
    {
        // Arrange
        GivenTemplates();
        var cut = Render<TemplateListPage>();
        cut.Find("[data-action=new]").Click();
        cut.Find("#msghatch-template-part").Change("1");
        cut.Find("#msghatch-template-language").Change("hu");
        cut.Find("#msghatch-template-new-company").Change("42");

        // Act
        cut.Find("[data-action=open-editor]").Click();

        // Assert
        Assert.Equal("http://localhost/msghatch/templates/Requests/hu/body/Html?companyId=42", Navigation.Uri);
    }

    [Fact]
    public void ClickOpenTheEditor_EmptyLanguage_ShowsValidationFailedAndStays()
    {
        // Arrange
        GivenTemplates();
        var cut = Render<TemplateListPage>();
        cut.Find("[data-action=new]").Click();
        cut.Find("#msghatch-template-language").Change(" ");

        // Act
        cut.Find("[data-action=open-editor]").Click();

        // Assert
        Assert.Equal("400", cut.Find("[data-form=new] ~ .alert-danger").GetAttribute("data-error-code"));
        Assert.Equal("http://localhost/", Navigation.Uri);
    }

    [Fact]
    public void Render_PartsUnavailable_ShowsTheTextOfUnavailableForTheNewTemplate()
    {
        // Arrange
        GivenTemplates();
        Queries.GetTemplatePartsAsync(null, Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<TemplatePartInfo>>(Errors.Unavailable));

        // Act
        var cut = Render<TemplateListPage>();
        cut.Find("[data-action=new]").Click();

        // Assert
        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".modal-body .alert-danger").TextContent);
        Assert.Empty(cut.FindAll("[data-action=open-editor]"));
    }

    [Fact]
    public void Render_NoDeclaredPart_ShowsThatNoMessageTypeDeclaresAPart()
    {
        // Arrange
        GivenTemplates();
        Queries.GetTemplatePartsAsync(null, Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<TemplatePartInfo>>([]));

        // Act
        var cut = Render<TemplateListPage>();
        cut.Find("[data-action=new]").Click();

        // Assert
        Assert.Single(cut.FindAll(".modal-body [data-state=no-parts]"));
        Assert.Empty(cut.FindAll("[data-action=open-editor]"));
    }

    [Fact]
    public void ClickNew_DeclaredParts_OpensTheDialogWithCancelOnTheLeftAndOpenTheEditorOnTheRight()
    {
        // Arrange
        GivenTemplates();
        var cut = Render<TemplateListPage>();
        Assert.Empty(cut.FindAll(".modal"));

        // Act
        cut.Find("[data-list-actions] [data-action=new]").Click();

        // Assert
        Assert.Equal(
            ("New template", "Cancel", "Open the editor", 2),
            (cut.Find(".modal-title").TextContent, cut.Find(".modal-footer [data-actions-start] button").TextContent,
                cut.Find(".modal-footer [data-actions-end] button").TextContent, cut.FindAll(".modal-body #msghatch-template-part option").Count));
    }

    [Fact]
    public void ClickCancel_OpenDialogWithAnError_ClosesTheDialogAndStays()
    {
        // Arrange
        GivenTemplates();
        var cut = Render<TemplateListPage>();
        cut.Find("[data-action=new]").Click();
        cut.Find("#msghatch-template-language").Change(" ");
        cut.Find("[data-action=open-editor]").Click();

        // Act
        cut.Find("[data-action=cancel-new]").Click();
        cut.Find("[data-action=new]").Click();

        // Assert
        Assert.Equal((0, "http://localhost/"), (cut.FindAll(".modal-body .alert-danger").Count, Navigation.Uri));
    }

    [Fact]
    public void ClickDelete_Row_ShowsTheQuestionWithTheDetailsOfThatRow()
    {
        // Arrange
        GivenTemplates(new TemplateEntry(Lookup(), 7), new TemplateEntry(Lookup("hu", "42", "body", TemplatePartKind.Html), 3));
        var cut = Render<TemplateListPage>();

        // Act
        cut.FindAll("tr[data-row]")[1].QuerySelector("[data-action=delete]")!.Click();

        // Assert
        Assert.Equal(
            ("Do you want to delete the body (Html) template of Requests in hu?", "Message type Requests | Language hu | Company 42 | Part body (Html) | Version 3", 1),
            (cut.Find(".modal-body p").TextContent,
                string.Join(" | ", cut.Find(".modal [data-delete-details] [data-metadata]").Children.Select(static item => item.TextContent)),
                cut.FindAll(".modal [data-delete-effect]").Count));
    }

    [Fact]
    public void ClickConfirmDelete_Row_DeletesTheTemplateOfThatRowAndLoadsTheListAgain()
    {
        // Arrange
        var company = Lookup("hu", "42", "body", TemplatePartKind.Html);
        GivenTemplates(new TemplateEntry(Lookup(), 7), new TemplateEntry(company, 3));
        Commands.DeleteTemplateAsync(company, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(null));
        var cut = Render<TemplateListPage>();
        cut.FindAll("tr[data-row]")[1].QuerySelector("[data-action=delete]")!.Click();

        // Act
        cut.Find("[data-action=confirm-delete]").Click();

        // Assert
        Commands.Received(1).DeleteTemplateAsync(company, Arg.Any<CancellationToken>());
        Queries.ReceivedWithAnyArgs(2).GetTemplatesAsync(default!, Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll(".modal"));
    }

    [Fact]
    public void ClickCancelDelete_Row_DeletesNothing()
    {
        // Arrange
        GivenTemplates(new TemplateEntry(Lookup(), 7));
        var cut = Render<TemplateListPage>();
        cut.Find("tr[data-row] [data-action=delete]").Click();

        // Act
        cut.Find("[data-action=cancel-delete]").Click();

        // Assert
        Assert.Empty(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().DeleteTemplateAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickConfirmDelete_Rejected_ShowsTheTextOfRejectedAboveTheList()
    {
        // Arrange
        GivenTemplates(new TemplateEntry(Lookup(), 7));
        Commands.DeleteTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(new Error(Errors.Rejected)));
        var cut = Render<TemplateListPage>();
        cut.Find("tr[data-row] [data-action=delete]").Click();

        // Act
        cut.Find("[data-action=confirm-delete]").Click();

        // Assert
        Assert.Equal((AdminErrors.RejectedText, 1), (cut.Find("[data-list-body] > .alert-danger").TextContent, cut.FindAll("tr[data-row]").Count));
    }

    private static string LanguagesOf(IRenderedComponent<TemplateListPage> cut) =>
        string.Join(' ', cut.FindAll("tr[data-row]").Select(static row => row.Children[1].TextContent));

    private void GivenTemplates(params TemplateEntry[] entries) =>
        Queries.GetTemplatesAsync(Arg.Any<TemplateQuery>(), Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<TemplateEntry>>(entries));
}
