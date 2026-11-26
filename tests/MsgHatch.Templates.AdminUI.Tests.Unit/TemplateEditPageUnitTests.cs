using Bunit;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Templates.AdminUI.DependencyInjection;
using MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Templates.Management;
using NSubstitute;
using static MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes.TemplateTestData;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class TemplateEditPageUnitTests : TemplatePageContext
{
    public TemplateEditPageUnitTests()
    {
        GivenParts(Part(engines: "liquid"));
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Ok(Source("Hello {{Name}}", 7)));
    }

    [Fact]
    public void Render_StoredTemplate_ShowsTheTextAndTheVersion()
    {
        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal("Hello {{Name}}", TextOf(cut));
        Assert.Equal("7", cut.Find("[data-field=version]").TextContent);
        Assert.Empty(cut.FindAll("[data-state=new]"));
    }

    [Fact]
    public void Render_NoStoredTemplate_ShowsANewTemplateWithVersion0AndNoDelete()
    {
        // Arrange
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Fail<TemplateSource>(Errors.NotFound));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal("0", cut.Find("[data-field=version]").TextContent);
        Assert.Single(cut.FindAll("[data-state=new]"));
        Assert.Empty(cut.FindAll("[data-action=delete]"));
    }

    [Fact]
    public void Render_StoredTemplate_PutsTheMetadataInTheHeaderAndTheActionsUnderTheScrollingBody()
    {
        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(
            (true, true, true, false),
            (cut.Find("[data-edit-header]").QuerySelector("[data-metadata]") is not null, cut.Find("[data-edit-body]").QuerySelector("[data-column=text]") is not null,
                cut.Find("[data-edit-actions]").QuerySelector("[data-action=save]") is not null, cut.Find("[data-edit-body]").QuerySelector("[data-action=save]") is not null));
    }

    [Fact]
    public void Render_StoredTemplate_HasTheUppercaseTitlesEditorAndLivePreviewAboveTheColumns()
    {
        // Act
        var cut = RenderPage();

        // Assert
        var editor = cut.Find("[data-column=text] > h2[data-column-title]");
        var preview = cut.Find("[data-column=preview] > h2[data-column-title]");
        Assert.Equal(
            ("Editor", "Live preview", true, true),
            (editor.TextContent, preview.TextContent, editor.ClassList.Contains("text-uppercase"), preview.ClassList.Contains("text-uppercase")));
        Assert.Equal("h2", cut.Find("[data-column=text]").FirstElementChild!.LocalName);
    }

    [Fact]
    public void Render_StoredTemplate_PutsBackOnTheLeftAndDeleteAndSaveOnTheRight()
    {
        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(
            ("back", TemplateAdminRoutes.List.TrimStart('/'), "delete save"),
            (cut.Find("[data-actions-start] a").GetAttribute("data-action"), cut.Find("[data-actions-start] a").GetAttribute("href"),
                string.Join(' ', cut.FindAll("[data-actions-end] button").Select(static button => button.GetAttribute("data-action")))));
    }

    [Fact]
    public void Render_StoredTemplateOfACompany_ShowsTheMetadataInOneRowOfLabelsAndValues()
    {
        // Arrange
        Queries.GetTemplateAsync(Lookup(scope: "42"), Arg.Any<CancellationToken>()).Returns(Ok(Source("For 42", 2)));
        Navigation.NavigateTo("msghatch/templates/Requests/en/subject/PlainText?companyId=42");

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(
            "Message type Requests | Language en | Company 42 | Part subject (PlainText) | Version 2",
            string.Join(" | ", cut.Find("[data-metadata]").Children.Select(static item => item.TextContent)));
        Assert.Empty(cut.FindAll("dl"));
    }

    [Fact]
    public void Input_ThreeChangesAndAPause_CallsThePreviewForTheStoredTextAndThenOneTimeForTheLastText()
    {
        // Arrange
        var cut = RenderPage();
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "Hello {{Name}}", Arg.Any<CancellationToken>());

        // Act
        cut.Find("textarea").Input("Hi");
        Clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Find("textarea").Input("Hi {{Name}}");
        Clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Find("textarea").Input("Hi {{Name}}!");
        Clock.Advance(TimeSpan.FromMilliseconds(499));
        Queries.ReceivedWithAnyArgs(1).PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>());

        Clock.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        cut.WaitForAssertion(() => Queries.ReceivedWithAnyArgs(2).PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>()));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), "Hi {{Name}}!", Arg.Any<CancellationToken>());
        Assert.Equal(("col-lg-6", "col-lg-6"), (cut.Find("[data-column=text]").ClassName, cut.Find("[data-column=preview]").ClassName));
    }

    [Fact]
    public void Render_PartWithNoSample_ShowsNoSampleDataAndCallsNoPreview()
    {
        // Arrange
        GivenParts(Part(engines: "liquid") with { HasSample = false });

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Single(cut.FindAll("[data-column=preview] [data-state=no-sample]"));
        Queries.DidNotReceiveWithAnyArgs().PreviewTemplateAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_TemplateWithAVersionText_ShowsTheTextOfTheStoreInPlaceOfTheNumber()
    {
        // Arrange
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Ok(Source("Hello", 7) with { VersionText = "2030-01-02 15:04:05 UTC" }));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal("2030-01-02 15:04:05 UTC", cut.Find("[data-metadata] [data-field=version]").TextContent);
    }

    [Fact]
    public void Render_CompanyInTheQuery_ReadsTheTemplateOfTheCompany()
    {
        // Arrange
        Queries.GetTemplateAsync(Lookup(scope: "42"), Arg.Any<CancellationToken>()).Returns(Ok(Source("For 42", 2)));
        Navigation.NavigateTo("msghatch/templates/Requests/en/subject/PlainText?companyId=42");

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal("For 42", TextOf(cut));
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
        Assert.Empty(cut.FindAll("[data-action=save]"));
    }

    [Fact]
    public void Render_PartsRejected_ShowsTheTextOfRejected()
    {
        // Arrange
        Queries.GetTemplatePartsAsync(Requests, Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<TemplatePartInfo>>(Errors.Rejected));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    [Theory]
    [InlineData("not a name", "en", "subject", "PlainText")]
    [InlineData("Requests", "en", "subject", "1")]
    [InlineData("Requests", "en", "sub ject", "PlainText")]
    public void Render_InvalidAddress_ShowsValidationFailedAndCallsNothing(string type, string language, string part, string kind)
    {
        // Act
        var cut = Render<TemplateEditPage>(p => p.Add(x => x.TypeName, type).Add(x => x.Language, language).Add(x => x.Part, part).Add(x => x.KindName, kind));

        // Assert
        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Queries.DidNotReceiveWithAnyArgs().GetTemplateAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Input_ChangedText_ShowsThePendingChanges()
    {
        // Arrange
        var cut = RenderPage();

        // Act
        cut.Find("textarea").Input("Hi {{Name}}");

        // Assert
        Assert.Single(cut.FindAll("[data-actions-middle] [data-state=pending]"));
    }

    [Fact]
    public void ClickSave_ChangedText_SendsTheTextWithTheExpectedVersionAndShowsTheNewVersion()
    {
        // Arrange
        Commands.SaveTemplateAsync(Lookup(), "Hi {{Name}}", 7, Arg.Any<CancellationToken>()).Returns(Ok(new TemplateEntry(Lookup(), 8)));
        var cut = RenderPage();
        cut.Find("textarea").Input("Hi {{Name}}");

        // Act
        cut.Find("[data-action=save]").Click();

        // Assert
        Commands.Received(1).SaveTemplateAsync(Lookup(), "Hi {{Name}}", 7, Arg.Any<CancellationToken>());
        Assert.Equal("8", cut.Find("[data-field=version]").TextContent);
        Assert.Single(cut.FindAll("[data-actions-middle] [data-state=saved]"));
        Assert.Empty(cut.FindAll("[data-state=pending]"));
    }

    [Fact]
    public void ClickSave_NewTemplate_SendsTheExpectedVersion0()
    {
        // Arrange
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Fail<TemplateSource>(Errors.NotFound));
        Commands.SaveTemplateAsync(Lookup(), "New", 0, Arg.Any<CancellationToken>()).Returns(Ok(new TemplateEntry(Lookup(), 1)));
        var cut = RenderPage();
        cut.Find("textarea").Input("New");

        // Act
        cut.Find("[data-action=save]").Click();

        // Assert
        Commands.Received(1).SaveTemplateAsync(Lookup(), "New", 0, Arg.Any<CancellationToken>());
        Assert.Single(cut.FindAll("[data-action=delete]"));
    }

    [Fact]
    public void ClickSave_Conflict_LoadsTheStoredVersionAgainAndShowsTheTextOfConflict()
    {
        // Arrange
        Commands.SaveTemplateAsync(Lookup(), "Mine", 7, Arg.Any<CancellationToken>()).Returns(Fail<TemplateEntry>(Errors.Conflict));
        var cut = RenderPage();
        cut.Find("textarea").Input("Mine");
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Ok(Source("Theirs", 9)));

        // Act
        cut.Find("[data-action=save]").Click();

        // Assert
        Assert.Equal("Theirs", TextOf(cut));
        Assert.Equal("9", cut.Find("[data-field=version]").TextContent);
        Assert.Equal(AdminErrors.ConflictText, cut.Find("[data-actions-middle] .alert-danger").TextContent);
    }

    [Fact]
    public void ClickSave_TemplateInvalid_ShowsTheMessageNextToTheText()
    {
        Commands.SaveTemplateAsync(Lookup(), "{{ Name", 7, Arg.Any<CancellationToken>()).Returns(Fail<TemplateEntry>(Errors.TemplateInvalid, "The tag has no end."));
        var cut = RenderPage();
        cut.Find("textarea").Input("{{ Name");

        cut.Find("[data-action=save]").Click();

        Assert.Equal("The tag has no end.", cut.Find("textarea ~ [data-issue]").TextContent);
        Assert.Empty(cut.FindAll(".alert-danger"));
    }

    [Fact]
    public void ClickSave_ValidationFailed_ShowsTheMessageInTheActionBar()
    {
        // Arrange
        Commands.SaveTemplateAsync(Lookup(), Arg.Any<string>(), 7, Arg.Any<CancellationToken>()).Returns(Fail<TemplateEntry>(Errors.ValidationFailed, "The store is not writable."));
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=save]").Click();

        // Assert
        Assert.Equal("The store is not writable.", cut.Find("[data-actions-middle] .alert-danger").TextContent);
    }

    [Fact]
    public void ClickSave_EditorStopsTheSave_SendsNoSave()
    {
        // Arrange
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");
        EditorSettings.AllowSave = false;
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=save]").Click();

        // Assert
        Commands.DidNotReceiveWithAnyArgs().SaveTemplateAsync(default, default!, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickDelete_StoredTemplate_ShowsItsMetadataAndTheFallbackInTheQuestion()
    {
        // Arrange
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=delete]").Click();

        // Assert
        Assert.Equal(
            "Message type Requests | Language en | Company All companies | Part subject (PlainText) | Version 7",
            string.Join(" | ", cut.Find(".modal [data-delete-details] [data-metadata]").Children.Select(static item => item.TextContent)));
        Assert.Equal((1, 1), (cut.FindAll(".modal [data-delete-effect]").Count, cut.FindAll(".modal [data-delete-warning]").Count));
    }

    [Fact]
    public void ClickDelete_NoAnswer_DeletesNothing()
    {
        // Arrange
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=delete]").Click();

        // Assert
        Assert.Single(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().DeleteTemplateAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickDelete_Cancelled_DeletesNothing()
    {
        // Arrange
        var cut = RenderPage();
        cut.Find("[data-action=delete]").Click();

        // Act
        cut.Find("[data-action=cancel-delete]").Click();

        // Assert
        Assert.Empty(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().DeleteTemplateAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickDelete_Confirmed_DeletesTheTemplateAndOpensTheList()
    {
        // Arrange
        Commands.DeleteTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(null));
        var cut = RenderPage();
        cut.Find("[data-action=delete]").Click();

        // Act
        cut.Find("[data-action=confirm-delete]").Click();

        // Assert
        Commands.Received(1).DeleteTemplateAsync(Lookup(), Arg.Any<CancellationToken>());
        Assert.Equal("http://localhost/msghatch/templates", Navigation.Uri);
    }

    [Fact]
    public void ClickDelete_ConfirmedAndRejected_ShowsTheTextOfRejectedAndStays()
    {
        // Arrange
        Commands.DeleteTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(new Error(Errors.Rejected)));
        var cut = RenderPage();
        cut.Find("[data-action=delete]").Click();

        // Act
        cut.Find("[data-action=confirm-delete]").Click();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find("[data-actions-middle] .alert-danger").TextContent);
        Assert.Equal("http://localhost/", Navigation.Uri);
    }

    [Fact]
    public void Render_PartWithOneEngineAndARegisteredEditor_ShowsThatEditor()
    {
        // Arrange
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal("Hello {{Name}}", cut.Find("[data-editor=test]").TextContent);
        Assert.Empty(cut.FindAll("[data-editor=plain]"));
    }

    [Fact]
    public void Render_TwoEditorsOfOneEngine_ShowsTheLaterEditor()
    {
        // Arrange
        Services.AddTemplateEditor<PlainTemplateEditor>("liquid");
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Single(cut.FindAll("[data-editor=test]"));
    }

    [Fact]
    public void Render_PartWithTwoEngines_ShowsThePlainEditor()
    {
        // Arrange
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");
        GivenParts(Part(engines: ["liquid", "placeholder"]));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Single(cut.FindAll("[data-editor=plain]"));
    }

    [Fact]
    public void Render_EngineWithNoEditor_ShowsThePlainEditor()
    {
        // Arrange
        Services.AddTemplateEditor<TestTemplateEditor>("other");

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Single(cut.FindAll("[data-editor=plain]"));
    }

    [Fact]
    public void Render_PartThatNoMessageTypeDeclares_ShowsThePlainEditor()
    {
        // Arrange
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");
        GivenParts(Part("body", TemplatePartKind.Html, "liquid"));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Single(cut.FindAll("[data-editor=plain]"));
    }

    [Fact]
    public void Input_ChangedText_MarksTheTextAreaWithAYellowBorder()
    {
        // Arrange
        var cut = RenderPage();
        var before = cut.Find("textarea").ClassList.Contains("border-warning");

        // Act
        cut.Find("textarea").Input("Hi {{Name}}");

        // Assert
        Assert.Equal((false, true), (before, cut.Find("textarea").ClassList.Contains("border-warning")));
    }

    private void GivenParts(params TemplatePartInfo[] parts) =>
        Queries.GetTemplatePartsAsync(Requests, Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<TemplatePartInfo>>(parts));

    private IRenderedComponent<TemplateEditPage> RenderPage() =>
        Render<TemplateEditPage>(p => p.Add(x => x.TypeName, "Requests").Add(x => x.Language, "en").Add(x => x.Part, "subject").Add(x => x.KindName, "PlainText"));

    private static string? TextOf(IRenderedComponent<TemplateEditPage> cut) => cut.Find("textarea").GetAttribute("value");
}
