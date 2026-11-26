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
    private readonly TemplateSource _stored = Source();

    public TemplateEditPageUnitTests()
    {
        GivenParts(Part(engines: "liquid"));
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Ok(_stored));
    }

    [Fact]
    public void Render_StoredTemplate_ShowsTheTextAndTheVersion()
    {
        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(_stored.Text, TextOf(cut));
        Assert.Equal($"{_stored.Key.Version}", cut.Find("[data-field=version]").TextContent);
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
        const string companyId = "42";
        var stored = Source("For 42", 2);
        Queries.GetTemplateAsync(Lookup(scope: companyId), Arg.Any<CancellationToken>()).Returns(Ok(stored));
        Navigation.NavigateTo($"msghatch/templates/Requests/en/subject/PlainText?companyId={companyId}");

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(
            $"Message type Requests | Language en | Company {companyId} | Part subject (PlainText) | Version {stored.Key.Version}",
            string.Join(" | ", cut.Find("[data-metadata]").Children.Select(static item => item.TextContent)));
        Assert.Empty(cut.FindAll("dl"));
    }

    [Fact]
    public void Input_ThreeChangesAndAPause_CallsThePreviewForTheStoredTextAndThenOneTimeForTheLastText()
    {
        // Arrange
        const string firstText = "Hi";
        const string secondText = "Hi {{Name}}";
        const string lastText = "Hi {{Name}}!";
        var cut = RenderPage();
        Queries.Received(1).PreviewTemplateAsync(Lookup(), _stored.Text, Arg.Any<CancellationToken>());

        // Act
        cut.Find("textarea").Input(firstText);
        Clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Find("textarea").Input(secondText);
        Clock.Advance(TimeSpan.FromMilliseconds(300));
        cut.Find("textarea").Input(lastText);
        Clock.Advance(TimeSpan.FromMilliseconds(499));
        Queries.DidNotReceive().PreviewTemplateAsync(Lookup(), Arg.Is<string>(text => text != _stored.Text), Arg.Any<CancellationToken>());

        Clock.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        cut.WaitForAssertion(() => Queries.Received(1).PreviewTemplateAsync(Lookup(), lastText, Arg.Any<CancellationToken>()));
        Assert.Equal(("col-lg-6", "col-lg-6"), (cut.Find("[data-column=text]").ClassName, cut.Find("[data-column=preview]").ClassName));
        Queries.Received(1).PreviewTemplateAsync(Lookup(), _stored.Text, Arg.Any<CancellationToken>());
        Queries.DidNotReceive().PreviewTemplateAsync(Lookup(), firstText, Arg.Any<CancellationToken>());
        Queries.DidNotReceive().PreviewTemplateAsync(Lookup(), secondText, Arg.Any<CancellationToken>());
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
        const string versionText = "2030-01-02 15:04:05 UTC";
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Ok(Source("Hello", 7) with { VersionText = versionText }));

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(versionText, cut.Find("[data-metadata] [data-field=version]").TextContent);
    }

    [Fact]
    public void Render_CompanyInTheQuery_ReadsTheTemplateOfTheCompany()
    {
        // Arrange
        const string companyId = "42";
        const string companyText = "For 42";
        Queries.GetTemplateAsync(Lookup(scope: companyId), Arg.Any<CancellationToken>()).Returns(Ok(Source(companyText, 2)));
        Navigation.NavigateTo($"msghatch/templates/Requests/en/subject/PlainText?companyId={companyId}");

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(companyText, TextOf(cut));
        Queries.Received(1).GetTemplateAsync(Lookup(scope: companyId), Arg.Any<CancellationToken>());
        Queries.DidNotReceive().GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>());
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
        Queries.DidNotReceiveWithAnyArgs().GetTemplateAsync(default, Arg.Any<CancellationToken>());
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
        Queries.DidNotReceiveWithAnyArgs().GetTemplatePartsAsync(default, Arg.Any<CancellationToken>());
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
        const string newText = "Hi {{Name}}";
        var saved = new TemplateEntry(Lookup(), 8);
        Commands.SaveTemplateAsync(Lookup(), newText, _stored.Key.Version, Arg.Any<CancellationToken>()).Returns(Ok(saved));
        var cut = RenderPage();
        cut.Find("textarea").Input(newText);

        // Act
        cut.Find("[data-action=save]").Click();

        // Assert
        Commands.Received(1).SaveTemplateAsync(Lookup(), newText, _stored.Key.Version, Arg.Any<CancellationToken>());
        Assert.Equal($"{saved.Version}", cut.Find("[data-field=version]").TextContent);
        Assert.Single(cut.FindAll("[data-actions-middle] [data-state=saved]"));
        Assert.Empty(cut.FindAll("[data-state=pending]"));
    }

    [Fact]
    public void ClickSave_NewTemplate_SendsTheExpectedVersion0()
    {
        // Arrange
        const string newText = "New";
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Fail<TemplateSource>(Errors.NotFound));
        Commands.SaveTemplateAsync(Lookup(), newText, 0, Arg.Any<CancellationToken>()).Returns(Ok(new TemplateEntry(Lookup(), 1)));
        var cut = RenderPage();
        cut.Find("textarea").Input(newText);

        // Act
        cut.Find("[data-action=save]").Click();

        // Assert
        Commands.Received(1).SaveTemplateAsync(Lookup(), newText, 0, Arg.Any<CancellationToken>());
        Assert.Single(cut.FindAll("[data-action=delete]"));
    }

    [Fact]
    public void ClickSave_Conflict_LoadsTheStoredVersionAgainAndShowsTheTextOfConflict()
    {
        // Arrange
        const string mine = "Mine";
        var theirs = Source("Theirs", 9);
        Commands.SaveTemplateAsync(Lookup(), mine, _stored.Key.Version, Arg.Any<CancellationToken>()).Returns(Fail<TemplateEntry>(Errors.Conflict));
        var cut = RenderPage();
        cut.Find("textarea").Input(mine);
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Ok(theirs));

        // Act
        cut.Find("[data-action=save]").Click();

        // Assert
        Assert.Equal(theirs.Text, TextOf(cut));
        Assert.Equal($"{theirs.Key.Version}", cut.Find("[data-field=version]").TextContent);
        Assert.Equal(AdminErrors.ConflictText, cut.Find("[data-actions-middle] .alert-danger").TextContent);
        Commands.Received(1).SaveTemplateAsync(Lookup(), mine, _stored.Key.Version, Arg.Any<CancellationToken>());
        Queries.Received(2).GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickSave_TemplateInvalid_ShowsTheMessageNextToTheText()
    {
        // Arrange
        const string invalidText = "{{ Name";
        const string message = "The tag has no end.";
        Commands.SaveTemplateAsync(Lookup(), invalidText, _stored.Key.Version, Arg.Any<CancellationToken>()).Returns(Fail<TemplateEntry>(Errors.TemplateInvalid, message));
        var cut = RenderPage();
        cut.Find("textarea").Input(invalidText);

        // Act
        cut.Find("[data-action=save]").Click();

        // Assert
        Assert.Equal(message, cut.Find("textarea ~ [data-issue]").TextContent);
        Assert.Empty(cut.FindAll(".alert-danger"));
        Commands.Received(1).SaveTemplateAsync(Lookup(), invalidText, _stored.Key.Version, Arg.Any<CancellationToken>());
        Queries.Received(1).GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickSave_ValidationFailed_ShowsTheMessageInTheActionBar()
    {
        // Arrange
        const string message = "The store is not writable.";
        Commands.SaveTemplateAsync(Lookup(), Arg.Any<string>(), _stored.Key.Version, Arg.Any<CancellationToken>()).Returns(Fail<TemplateEntry>(Errors.ValidationFailed, message));
        var cut = RenderPage();

        // Act
        cut.Find("[data-action=save]").Click();

        // Assert
        Assert.Equal(message, cut.Find("[data-actions-middle] .alert-danger").TextContent);
        Commands.Received(1).SaveTemplateAsync(Lookup(), _stored.Text, _stored.Key.Version, Arg.Any<CancellationToken>());
        Queries.Received(1).GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>());
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
            $"Message type Requests | Language en | Company All companies | Part subject (PlainText) | Version {_stored.Key.Version}",
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
        Commands.Received(1).DeleteTemplateAsync(Lookup(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_PartWithOneEngineAndARegisteredEditor_ShowsThatEditor()
    {
        // Arrange
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");

        // Act
        var cut = RenderPage();

        // Assert
        Assert.Equal(_stored.Text, cut.Find("[data-editor=test]").TextContent);
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
