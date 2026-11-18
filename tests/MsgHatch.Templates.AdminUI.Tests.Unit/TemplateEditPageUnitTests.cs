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
        var cut = RenderPage();

        Assert.Equal("Hello {{Name}}", TextOf(cut));
        Assert.Equal("7", cut.Find("[data-field=version]").TextContent);
        Assert.Empty(cut.FindAll("[data-state=new]"));
    }

    [Fact]
    public void Render_NoStoredTemplate_ShowsANewTemplateWithVersion0AndNoDelete()
    {
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Fail<TemplateSource>(Errors.NotFound));

        var cut = RenderPage();

        Assert.Equal("0", cut.Find("[data-field=version]").TextContent);
        Assert.Single(cut.FindAll("[data-state=new]"));
        Assert.Empty(cut.FindAll("[data-action=delete]"));
    }

    [Fact]
    public void Render_CompanyInTheQuery_ReadsTheTemplateOfTheCompany()
    {
        Queries.GetTemplateAsync(Lookup(scope: "42"), Arg.Any<CancellationToken>()).Returns(Ok(Source("For 42", 2)));
        Navigation.NavigateTo("msghatch/templates/Requests/en/subject/PlainText?companyId=42");

        var cut = RenderPage();

        Assert.Equal("For 42", TextOf(cut));
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailable()
    {
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Fail<TemplateSource>(Errors.Unavailable));

        var cut = RenderPage();

        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
        Assert.Empty(cut.FindAll("[data-action=save]"));
    }

    [Fact]
    public void Render_PartsRejected_ShowsTheTextOfRejected()
    {
        Queries.GetTemplatePartsAsync(Requests, Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<TemplatePartInfo>>(Errors.Rejected));

        var cut = RenderPage();

        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    [Theory]
    [InlineData("not a name", "en", "subject", "PlainText")]
    [InlineData("Requests", "en", "subject", "1")]
    [InlineData("Requests", "en", "sub ject", "PlainText")]
    public void Render_InvalidAddress_ShowsValidationFailedAndCallsNothing(string type, string language, string part, string kind)
    {
        var cut = Render<TemplateEditPage>(p => p.Add(x => x.TypeName, type).Add(x => x.Language, language).Add(x => x.Part, part).Add(x => x.KindName, kind));

        Assert.Equal("400", cut.Find(".alert-danger").GetAttribute("data-error-code"));
        Queries.DidNotReceiveWithAnyArgs().GetTemplateAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Input_ChangedText_ShowsThePendingChanges()
    {
        var cut = RenderPage();

        cut.Find("textarea").Input("Hi {{Name}}");

        Assert.Single(cut.FindAll("[data-state=pending]"));
    }

    [Fact]
    public void ClickSave_ChangedText_SendsTheTextWithTheExpectedVersionAndShowsTheNewVersion()
    {
        Commands.SaveTemplateAsync(Lookup(), "Hi {{Name}}", 7, Arg.Any<CancellationToken>()).Returns(Ok(new TemplateEntry(Lookup(), 8)));
        var cut = RenderPage();
        cut.Find("textarea").Input("Hi {{Name}}");

        cut.Find("[data-action=save]").Click();

        Commands.Received(1).SaveTemplateAsync(Lookup(), "Hi {{Name}}", 7, Arg.Any<CancellationToken>());
        Assert.Equal("8", cut.Find("[data-field=version]").TextContent);
        Assert.Single(cut.FindAll("[data-state=saved]"));
        Assert.Empty(cut.FindAll("[data-state=pending]"));
    }

    [Fact]
    public void ClickSave_NewTemplate_SendsTheExpectedVersion0()
    {
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Fail<TemplateSource>(Errors.NotFound));
        Commands.SaveTemplateAsync(Lookup(), "New", 0, Arg.Any<CancellationToken>()).Returns(Ok(new TemplateEntry(Lookup(), 1)));
        var cut = RenderPage();
        cut.Find("textarea").Input("New");

        cut.Find("[data-action=save]").Click();

        Commands.Received(1).SaveTemplateAsync(Lookup(), "New", 0, Arg.Any<CancellationToken>());
        Assert.Single(cut.FindAll("[data-action=delete]"));
    }

    [Fact]
    public void ClickSave_Conflict_LoadsTheStoredVersionAgainAndShowsTheTextOfConflict()
    {
        Commands.SaveTemplateAsync(Lookup(), "Mine", 7, Arg.Any<CancellationToken>()).Returns(Fail<TemplateEntry>(Errors.Conflict));
        var cut = RenderPage();
        cut.Find("textarea").Input("Mine");
        Queries.GetTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(Ok(Source("Theirs", 9)));

        cut.Find("[data-action=save]").Click();

        Assert.Equal("Theirs", TextOf(cut));
        Assert.Equal("9", cut.Find("[data-field=version]").TextContent);
        Assert.Equal(AdminErrors.ConflictText, cut.Find(".alert-danger").TextContent);
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
    public void ClickSave_ValidationFailed_ShowsTheMessageNextToTheForm()
    {
        Commands.SaveTemplateAsync(Lookup(), Arg.Any<string>(), 7, Arg.Any<CancellationToken>()).Returns(Fail<TemplateEntry>(Errors.ValidationFailed, "The store is not writable."));
        var cut = RenderPage();

        cut.Find("[data-action=save]").Click();

        Assert.Equal("The store is not writable.", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void ClickSave_EditorStopsTheSave_SendsNoSave()
    {
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");
        EditorSettings.AllowSave = false;
        var cut = RenderPage();

        cut.Find("[data-action=save]").Click();

        Commands.DidNotReceiveWithAnyArgs().SaveTemplateAsync(default, default!, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickDelete_NoAnswer_DeletesNothing()
    {
        var cut = RenderPage();

        cut.Find("[data-action=delete]").Click();

        Assert.Single(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().DeleteTemplateAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickDelete_Cancelled_DeletesNothing()
    {
        var cut = RenderPage();
        cut.Find("[data-action=delete]").Click();

        cut.Find("[data-action=cancel-delete]").Click();

        Assert.Empty(cut.FindAll(".modal"));
        Commands.DidNotReceiveWithAnyArgs().DeleteTemplateAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickDelete_Confirmed_DeletesTheTemplateAndOpensTheList()
    {
        Commands.DeleteTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(null));
        var cut = RenderPage();
        cut.Find("[data-action=delete]").Click();

        cut.Find("[data-action=confirm-delete]").Click();

        Commands.Received(1).DeleteTemplateAsync(Lookup(), Arg.Any<CancellationToken>());
        Assert.Equal("http://localhost/msghatch/templates", Navigation.Uri);
    }

    [Fact]
    public void ClickDelete_ConfirmedAndRejected_ShowsTheTextOfRejectedAndStays()
    {
        Commands.DeleteTemplateAsync(Lookup(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Error?>(new Error(Errors.Rejected)));
        var cut = RenderPage();
        cut.Find("[data-action=delete]").Click();

        cut.Find("[data-action=confirm-delete]").Click();

        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
        Assert.Equal("http://localhost/", Navigation.Uri);
    }

    [Fact]
    public void Render_PartWithOneEngineAndARegisteredEditor_ShowsThatEditor()
    {
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");

        var cut = RenderPage();

        Assert.Equal("Hello {{Name}}", cut.Find("[data-editor=test]").TextContent);
        Assert.Empty(cut.FindAll("[data-editor=plain]"));
    }

    [Fact]
    public void Render_TwoEditorsOfOneEngine_ShowsTheLaterEditor()
    {
        Services.AddTemplateEditor<PlainTemplateEditor>("liquid");
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");

        var cut = RenderPage();

        Assert.Single(cut.FindAll("[data-editor=test]"));
    }

    [Fact]
    public void Render_PartWithTwoEngines_ShowsThePlainEditor()
    {
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");
        GivenParts(Part(engines: ["liquid", "placeholder"]));

        var cut = RenderPage();

        Assert.Single(cut.FindAll("[data-editor=plain]"));
    }

    [Fact]
    public void Render_EngineWithNoEditor_ShowsThePlainEditor()
    {
        Services.AddTemplateEditor<TestTemplateEditor>("other");

        var cut = RenderPage();

        Assert.Single(cut.FindAll("[data-editor=plain]"));
    }

    [Fact]
    public void Render_PartThatNoMessageTypeDeclares_ShowsThePlainEditor()
    {
        Services.AddTemplateEditor<TestTemplateEditor>("liquid");
        GivenParts(Part("body", TemplatePartKind.Html, "liquid"));

        var cut = RenderPage();

        Assert.Single(cut.FindAll("[data-editor=plain]"));
    }

    private void GivenParts(params TemplatePartInfo[] parts) =>
        Queries.GetTemplatePartsAsync(Requests, Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<TemplatePartInfo>>(parts));

    private IRenderedComponent<TemplateEditPage> RenderPage() =>
        Render<TemplateEditPage>(p => p.Add(x => x.TypeName, "Requests").Add(x => x.Language, "en").Add(x => x.Part, "subject").Add(x => x.KindName, "PlainText"));

    private static string? TextOf(IRenderedComponent<TemplateEditPage> cut) => cut.Find("textarea").GetAttribute("value");
}
