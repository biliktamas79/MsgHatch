using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Templates;
using MsgHatch.Channels.Email.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Channels.Email.Tests.Unit.TestTypes.TestRoutes;

namespace MsgHatch.Channels.Email.Tests.Unit;

/// <summary>The email composer (section 4.8, step 4), with a loader and an engine substitute.</summary>
public sealed class EmailComposerUnitTests
{
    private static readonly MessageType _type = new("TestEvent");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ComposerDoubles _doubles = new();

    [Fact]
    public async Task ComposeAsync_ThreeRoutes_CallsTheLoaderOneTimeWithAllRoutes()
    {
        // Arrange
        MessageRoute[] routes = [AnEmailRoute("u1"), AnEmailRoute("u2", "hu"), AnEmailRoute("u3")];

        // Act
        await _doubles.ComposeAsync(routes);

        // Assert
        Assert.Equal([routes], _doubles.LoadCalls);
    }

    [Fact]
    public async Task ComposeAsync_RoutesOfTwoLanguagesAndTwoScopes_CallsPrepareOneTimeForEachGroupInTheOrderOfItsFirstRoute()
    {
        // Arrange
        const string english = "en";
        const string hungarian = "hu";
        const string scope = "acme";
        MessageRoute[] routes =
        [
            AnEmailRoute("u1", english), AnEmailRoute("u2", hungarian), AnEmailRoute("u3", english), AnEmailRoute("u4", english, templateScope: scope),
        ];

        // Act
        await _doubles.ComposeAsync(routes);

        // Assert
        Assert.Equal(
            [(english, null), (hungarian, null), (english, scope)],
            _doubles.Prepares.Select(static request => (request.Language, request.Scope)).Distinct());
        Assert.Equal(9, _doubles.Prepares.Count);
    }

    [Fact]
    public async Task ComposeAsync_OneGroup_PreparesTheSubjectTheHtmlBodyAndTheOptionalTextBody()
    {
        // Act
        await _doubles.ComposeAsync(AnEmailRoute("u1"), AnEmailRoute("u2"));

        // Assert
        Assert.Equal(
            [
                new TemplateRequest(_type, "en", null, "subject", TemplatePartKind.PlainText),
                new TemplateRequest(_type, "en", null, "body", TemplatePartKind.Html),
                new TemplateRequest(_type, "en", null, "body", TemplatePartKind.PlainText, Optional: true),
            ],
            _doubles.Prepares);
    }

    [Fact]
    public async Task ComposeAsync_HtmlBodyFoundInTheDefaultLanguageWithNoScope_PreparesTheOptionalTextBodyInThatLanguageAndScope()
    {
        // Arrange
        const string defaultLanguage = "en";
        const string language = "hu";
        const string scope = "acme";
        _doubles.LookupOf = static request => request.Kind == TemplatePartKind.Html
            ? new TemplateLookup(request.Type, defaultLanguage, Scope: null, request.Part, request.Kind)
            : new TemplateLookup(request.Type, request.Language, request.Scope, request.Part, request.Kind);

        // Act
        await _doubles.ComposeAsync(AnEmailRoute("u1", language, templateScope: scope));

        // Assert
        Assert.Equal(
            [
                new TemplateRequest(_type, language, scope, "subject", TemplatePartKind.PlainText),
                new TemplateRequest(_type, language, scope, "body", TemplatePartKind.Html),
                new TemplateRequest(_type, defaultLanguage, null, "body", TemplatePartKind.PlainText, Optional: true),
            ],
            _doubles.Prepares);
    }

    [Fact]
    public async Task ComposeAsync_TwoRoutes_GivesAnEmailToTheAddressOfEachRouteWithItsRenderedParts()
    {
        // Arrange
        const string firstAddress = "client1@example.com";
        const string secondAddress = "client2@example.com";
        MessageRoute[] routes = [AnEmailRoute("u1", address: firstAddress), AnEmailRoute("u2", address: secondAddress)];

        // Act
        var result = await _doubles.ComposeAsync(routes);

        // Assert
        Assert.Equal(
            [
                new EmailMessage(firstAddress, "Subject u1", "<p>u1</p>", "Text u1"),
                new EmailMessage(secondAddress, "Subject u2", "<p>u2</p>", "Text u2"),
            ],
            result.Value.Select(static email => email.Value));
    }

    [Fact]
    public async Task ComposeAsync_RoutesOfTwoLanguages_RendersEachRouteWithItsPayloadAndTheCultureOfItsLanguage()
    {
        // Arrange
        const string english = "en";
        const string hungarian = "hu-HU";
        MessageRoute[] routes = [AnEmailRoute("u1", english), AnEmailRoute("u2", hungarian), AnEmailRoute("u3", english)];

        // Act
        await _doubles.ComposeAsync(routes);

        // Assert
        Assert.Equal(
            [("u1", english), ("u3", english), ("u2", hungarian)],
            _doubles.Renders.Select(static render => (render.Payload.Name, render.Culture.Name)).Distinct());
    }

    [Fact]
    public async Task ComposeAsync_RouteWithNoAddress_FailsThatRouteWithValidationFailedAndGivesTheOthersTheirEmails()
    {
        // Arrange
        MessageRoute[] routes = [AnEmailRoute("u1"), AnEmailRoute("u2", address: null), AnEmailRoute("u3", address: " ")];

        // Act
        var result = await _doubles.ComposeAsync(routes);

        // Assert
        Assert.Equal([0, 400, 400], result.Value.Select(CodeOf));
    }

    [Fact]
    public async Task ComposeAsync_LoaderError_IsTheOuterError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        _doubles.LoadResult = _ => error;

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"), AnEmailRoute("u2"));

        // Assert
        Assert.Same(error, result.Error);
        await _doubles.Engine.DidNotReceiveWithAnyArgs().PrepareAsync(default!, Ct);
    }

    [Fact]
    public async Task ComposeAsync_LoaderGivesAnotherNumberOfPayloads_FailsWithAnOuterValidationFailed()
    {
        // Arrange
        _doubles.LoadResult = static _ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([new TestPayload("u1")]);

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"), AnEmailRoute("u2"));

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
        await _doubles.Engine.DidNotReceiveWithAnyArgs().PrepareAsync(default!, Ct);
    }

    [Fact]
    public async Task ComposeAsync_LoaderGivesANullPayload_FailsThatRouteWithValidationFailed()
    {
        // Arrange
        _doubles.LoadResult = static _ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([new TestPayload("u1"), null!]);

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"), AnEmailRoute("u2"));

        // Assert
        Assert.Equal([0, 400], result.Value.Select(CodeOf));
    }

    [Fact]
    public async Task ComposeAsync_MissingTemplateOfOneGroup_FailsOnlyTheMessagesOfThatGroupWithNotFound()
    {
        // Arrange
        _doubles.PrepareResult = request => request.Language == "hu" ? new Error(Errors.NotFound) : _doubles.TheTemplate(request);

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1", "en"), AnEmailRoute("u2", "hu"), AnEmailRoute("u3", "en"), AnEmailRoute("u4", "hu"));

        // Assert
        Assert.Equal([0, 404, 0, 404], result.Value.Select(CodeOf));
        await _doubles.Engine.Received(1).PrepareAsync(new TemplateRequest(_type, "hu", null, "subject", TemplatePartKind.PlainText), Ct);
        await _doubles.Engine.DidNotReceive().PrepareAsync(Arg.Is<TemplateRequest>(static request => request.Language == "hu" && request.Part == "body"), Ct);
    }

    [Fact]
    public async Task ComposeAsync_UnknownLanguage_FailsOnlyTheMessagesOfThatGroupWithValidationFailedAndPreparesNoTemplateForIt()
    {
        // Arrange
        MessageRoute[] routes = [AnEmailRoute("u1", "en"), AnEmailRoute("u2", "xx-unknown")];

        // Act
        var result = await _doubles.ComposeAsync(routes);

        // Assert
        Assert.Equal([0, 400], result.Value.Select(CodeOf));
        Assert.Equal(["en"], _doubles.Prepares.Select(static request => request.Language).Distinct());
    }

    [Fact]
    public async Task ComposeAsync_RenderErrorForOneRoute_FailsOnlyThatRoute()
    {
        // Arrange
        _doubles.RenderResult = static (_, payload) => payload.Name == "u2" ? new Error(Errors.TemplateInvalid) : "part";

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"), AnEmailRoute("u2"));

        // Assert
        Assert.Equal([0, 422], result.Value.Select(CodeOf));
    }

    [Fact]
    public async Task ComposeAsync_PlainTextFormat_GivesOnlyTheTextBody()
    {
        // Arrange
        var route = AnEmailRoute("u1", format: EmailFormat.PlainText);

        // Act
        var result = await _doubles.ComposeAsync(route);

        // Assert
        Assert.Equal(new EmailMessage("client1@example.com", "Subject u1", null, "Text u1"), Assert.Single(result.Value).Value);
        Assert.DoesNotContain(_doubles.Renders, static render => render.Request.Kind == TemplatePartKind.Html);
    }

    [Fact]
    public async Task ComposeAsync_HtmlFormatAndNoTextPart_GivesOnlyTheHtmlBody()
    {
        // Arrange
        _doubles.PrepareResult = NoTextPart;

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"));

        // Assert
        Assert.Equal(new EmailMessage("client1@example.com", "Subject u1", "<p>u1</p>", null), Assert.Single(result.Value).Value);
    }

    [Fact]
    public async Task ComposeAsync_PlainTextFormatAndNoTextPart_FailsThatRouteWithTemplateInvalid()
    {
        // Arrange
        _doubles.PrepareResult = NoTextPart;

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"), AnEmailRoute("u2", format: EmailFormat.PlainText));

        // Assert
        Assert.Equal([0, 422], result.Value.Select(CodeOf));
    }

    [Theory]
    [InlineData("subject", TemplatePartKind.PlainText)]
    [InlineData("body", TemplatePartKind.Html)]
    [InlineData("body", TemplatePartKind.PlainText)]
    public async Task ComposeAsync_RenderErrorOfOnePart_FailsTheRouteWithThatError(string part, TemplatePartKind kind)
    {
        // Arrange
        var defaultRender = _doubles.RenderResult;
        _doubles.RenderResult = (request, payload) =>
            request.Part == part && request.Kind == kind ? new Error(Errors.TemplateInvalid) : defaultRender(request, payload);

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"));

        // Assert
        Assert.Equal([422], result.Value.Select(CodeOf));
    }

    [Theory]
    [InlineData("subject", TemplatePartKind.PlainText)]
    [InlineData("body", TemplatePartKind.Html)]
    public async Task ComposeAsync_MissingRequiredPart_FailsTheGroupWithNotFound(string part, TemplatePartKind kind)
    {
        // Arrange
        _doubles.PrepareResult = request => request.Part == part && request.Kind == kind ? new Error(Errors.NotFound) : _doubles.TheTemplate(request);

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"));

        // Assert
        Assert.Equal([404], result.Value.Select(CodeOf));
        await _doubles.Engine.Received(1).PrepareAsync(new TemplateRequest(_type, "en", null, part, kind), Ct);
        await _doubles.Engine.DidNotReceive().PrepareAsync(Arg.Is<TemplateRequest>(static request => request.Optional), Ct);
    }

    [Fact]
    public async Task ComposeAsync_TextPartWithAnErrorThatIsNotNotFound_FailsTheGroupWithThatError()
    {
        // Arrange
        _doubles.PrepareResult = request => request.Part == "body" && request.Kind == TemplatePartKind.PlainText
            ? new Error(Errors.Unavailable)
            : _doubles.TheTemplate(request);

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"));

        // Assert
        Assert.Equal([Errors.Unavailable.Code], result.Value.Select(CodeOf));
    }

    private ErrorOr<ITemplate<TestPayload>> NoTextPart(TemplateRequest request) =>
        request.Part == "body" && request.Kind == TemplatePartKind.PlainText ? new Error(Errors.NotFound) : _doubles.TheTemplate(request);

    private static int CodeOf(ErrorOr<EmailMessage> email) => email.IsError ? email.Error.Type.Code : 0;
}
