using System.Globalization;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Tests.Unit.Contracts;

public abstract partial class TemplateEngineTestsBase<TEngine>
{
    [Fact]
    public async Task PrepareAsync_MissingTemplate_GivesNotFound()
    {
        // Act
        var result = await Engine.PrepareAsync(ARequest(), Ct);

        // Assert
        Assert.Equal(404, CodeOf(result));
    }

    [Fact]
    public async Task PrepareAsync_TemplateOfAnotherKind_GivesNotFound()
    {
        // Arrange
        TestData.Seed(Type, "en", Part, TemplatePartKind.Html, "<p>{Name}</p>");

        // Act
        var result = await Engine.PrepareAsync(ARequest(kind: TemplatePartKind.PlainText), Ct);

        // Assert
        Assert.Equal(404, CodeOf(result));
    }

    [Fact]
    public async Task PrepareAsync_TemplateOfTheDefaultLanguage_GivesTheLookupThatFoundIt()
    {
        // Arrange
        TestData.Seed(Type, "en", Part, TemplatePartKind.PlainText, "Order {Name}");

        // Act
        var result = await Engine.PrepareAsync(ARequest("hu"), Ct);

        // Assert
        Assert.Equal(new TemplateLookup(Type, "en", Scope: null, Part, TemplatePartKind.PlainText), result.Value?.Lookup);
    }

    [Fact]
    public async Task PrepareAsync_ExistingTemplate_GivesATemplate()
    {
        // Arrange
        TestData.Seed(Type, "en", Part, TemplatePartKind.PlainText, "Order {Name}");
        var payload = APayload();

        // Act
        var result = await Engine.PrepareAsync(ARequest(), Ct);

        // Assert
        var rendered = await result.Value.RenderAsync(payload, CultureInfo.GetCultureInfo("en"), Ct);
        Assert.Equal(
            (new TemplateLookup(Type, "en", Scope: null, Part, TemplatePartKind.PlainText), $"Order {payload.Name}"),
            (result.Value.Lookup, rendered.Value));
    }
}
