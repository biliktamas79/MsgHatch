using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Tests.Unit.TestTypes;
using static MsgHatch.Email.Tests.Unit.TestTypes.TestRoutes;

namespace MsgHatch.Email.Tests.Unit;

/// <summary>The email composer (section 4.8, step 4), with a loader and an engine substitute.</summary>
public sealed class EmailComposerUnitTests
{
    private static readonly MessageType _type = new("TestEvent");

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
        MessageRoute[] routes =
        [
            AnEmailRoute("u1", "en"), AnEmailRoute("u2", "hu"), AnEmailRoute("u3", "en"), AnEmailRoute("u4", "en", templateScope: "acme"),
        ];

        // Act
        await _doubles.ComposeAsync(routes);

        // Assert
        Assert.Equal(
            [new EmailTemplateRequest(_type, "en", null), new EmailTemplateRequest(_type, "hu", null), new EmailTemplateRequest(_type, "en", "acme")],
            _doubles.Prepares);
    }

    [Fact]
    public async Task ComposeAsync_TwoRoutes_GivesAnEmailToTheAddressOfEachRouteWithItsRenderedParts()
    {
        // Arrange
        MessageRoute[] routes = [AnEmailRoute("u1", address: "client1@example.com"), AnEmailRoute("u2", address: "client2@example.com")];

        // Act
        var result = await _doubles.ComposeAsync(routes);

        // Assert
        Assert.Equal(
            [
                new EmailMessage("client1@example.com", "Subject u1", "<p>u1</p>", "Text u1"),
                new EmailMessage("client2@example.com", "Subject u2", "<p>u2</p>", "Text u2"),
            ],
            result.Value.Select(static email => email.Value));
    }

    [Fact]
    public async Task ComposeAsync_RoutesOfTwoLanguages_RendersEachRouteWithItsPayloadAndTheCultureOfItsLanguage()
    {
        // Arrange
        MessageRoute[] routes = [AnEmailRoute("u1", "en"), AnEmailRoute("u2", "hu-HU"), AnEmailRoute("u3", "en")];

        // Act
        await _doubles.ComposeAsync(routes);

        // Assert
        Assert.Equal(
            [("u1", "en"), ("u3", "en"), ("u2", "hu-HU")],
            _doubles.Renders.Select(static render => (render.Payload.Name, render.Culture.Name)));
    }

    [Fact]
    public async Task ComposeAsync_RouteWithATrace_RendersWithTheRouteAndItsTraceUnchanged()
    {
        // Arrange
        var trace = new TestTrace("TestRouter");
        var route = AnEmailRoute("u1", trace: trace);

        // Act
        await _doubles.ComposeAsync(route);

        // Assert
        var render = Assert.Single(_doubles.Renders);
        Assert.Equal(route, render.Route);
        Assert.Same(trace, render.Route.Trace);
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
        Assert.Equal(["en"], _doubles.Prepares.Select(static request => request.Language));
    }

    [Fact]
    public async Task ComposeAsync_RenderErrorForOneRoute_FailsOnlyThatRoute()
    {
        // Arrange
        _doubles.RenderResult = static route => route.Target.Id == "u2"
            ? new Error(Errors.TemplateInvalid)
            : new RenderedEmail("Subject", "<p>Html</p>", null);

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
    }

    [Fact]
    public async Task ComposeAsync_HtmlFormatAndNoTextPart_GivesOnlyTheHtmlBody()
    {
        // Arrange
        _doubles.RenderResult = static _ => new RenderedEmail("Subject", "<p>Html</p>", null);

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"));

        // Assert
        Assert.Equal(new EmailMessage("client1@example.com", "Subject", "<p>Html</p>", null), Assert.Single(result.Value).Value);
    }

    [Fact]
    public async Task ComposeAsync_PlainTextFormatAndNoTextPart_FailsThatRouteWithTemplateInvalid()
    {
        // Arrange
        _doubles.RenderResult = static _ => new RenderedEmail("Subject", "<p>Html</p>", null);

        // Act
        var result = await _doubles.ComposeAsync(AnEmailRoute("u1"), AnEmailRoute("u2", format: EmailFormat.PlainText));

        // Assert
        Assert.Equal([0, 422], result.Value.Select(CodeOf));
    }

    private static int CodeOf(ErrorOr<EmailMessage> email) => email.IsError ? email.Error.Type.Code : 0;
}
