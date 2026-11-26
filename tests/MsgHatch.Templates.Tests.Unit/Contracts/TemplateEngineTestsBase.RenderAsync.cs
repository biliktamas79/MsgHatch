using System.Globalization;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Tests.Unit.Contracts;

public abstract partial class TemplateEngineTestsBase<TEngine>
{
    [Fact]
    public async Task RenderAsync_TemplateWithAnUnknownName_GivesTemplateInvalidAtPrepareOrRender()
    {
        // Arrange
        TestData.Seed(Type, "en", Part, TemplatePartKind.PlainText, "Order {Unknown}");

        // Act
        var template = await Engine.PrepareAsync(ARequest(), Ct);
        var code = template.IsError
            ? CodeOf(template)
            : CodeOf(await template.Value.RenderAsync(APayload(), CultureInfo.GetCultureInfo("en"), Ct));

        // Assert
        Assert.Equal(422, code);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("hu-HU")]
    public async Task RenderAsync_PayloadValues_RenderInTheCultureOfTheRequest(string language)
    {
        // Arrange
        var culture = CultureInfo.GetCultureInfo(language);
        TestData.Seed(Type, language, Part, TemplatePartKind.PlainText, "Order {Count} of {Name}: {Total} on {ShippedAt}");
        var payload = APayload(name: "Client 1", count: 3, total: 1234.5m);

        // Act
        var rendered = await RenderAsync(payload, language);

        // Assert
        Assert.Equal($"Order {payload.Count} of {payload.Name}: {payload.Total.ToString(culture)} on {payload.ShippedAt.ToString(culture)}", rendered);
    }

    [Theory]
    [InlineData(TemplatePartKind.Html, "<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>")]
    [InlineData(TemplatePartKind.PlainText, "<p><script>alert(1)</script></p>")]
    public async Task RenderAsync_ValueWithAScriptTag_IsEncodedOnlyInAnHtmlPart(TemplatePartKind kind, string expected)
    {
        // Arrange
        TestData.Seed(Type, "en", Part, kind, "<p>{Name}</p>");

        // Act
        var rendered = await RenderAsync(APayload(name: "<script>alert(1)</script>"), "en", kind);

        // Assert
        Assert.Equal(expected, rendered);
    }

    [Theory]
    [InlineData(TemplatePartKind.Html)]
    [InlineData(TemplatePartKind.PlainText)]
    public async Task RenderAsync_AccentedCharacters_RenderUnchanged(TemplatePartKind kind)
    {
        // Arrange
        TestData.Seed(Type, "hu", Part, kind, "<p>Kedves {Name}, ő ű</p>");

        // Act
        var rendered = await RenderAsync(APayload(name: "Ödön Űr"), "hu", kind);

        // Assert
        Assert.Equal("<p>Kedves Ödön Űr, ő ű</p>", rendered);
    }
}
