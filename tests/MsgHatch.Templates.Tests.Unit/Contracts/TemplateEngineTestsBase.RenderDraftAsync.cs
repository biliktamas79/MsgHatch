using System.Globalization;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Tests.Unit.Contracts;

public abstract partial class TemplateEngineTestsBase<TEngine>
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("hu-HU")]
    public async Task RenderAsync_Draft_RendersThePayloadValuesInTheCultureOfTheLanguage(string language)
    {
        // Arrange
        var culture = CultureInfo.GetCultureInfo(language);
        var text = TestData.InEngineSyntax("Order {Count} of {Name}: {Total} on {ShippedAt}");
        var lookup = ALookup() with { Language = language };
        var payload = APayload(name: "Client 1", count: 3, total: 1234.5m);

        // Act
        var rendered = await Engine.RenderAsync(lookup, text, payload, Ct);

        // Assert
        Assert.Equal($"Order {payload.Count} of {payload.Name}: {payload.Total.ToString(culture)} on {payload.ShippedAt.ToString(culture)}", rendered.Value);
    }

    [Theory]
    [InlineData(TemplatePartKind.Html, "<p>&lt;script&gt;alert(1)&lt;/script&gt; Ödön</p>")]
    [InlineData(TemplatePartKind.PlainText, "<p><script>alert(1)</script> Ödön</p>")]
    public async Task RenderAsync_DraftWithAScriptTag_IsEncodedOnlyInAnHtmlPart(TemplatePartKind kind, string expected)
    {
        // Act
        var rendered = await Engine.RenderAsync(ALookup(kind), TestData.InEngineSyntax("<p>{Name}</p>"), APayload(name: "<script>alert(1)</script> Ödön"), Ct);

        // Assert
        Assert.Equal(expected, rendered.Value);
    }

    [Fact]
    public async Task RenderAsync_DraftThatDoesNotParse_GivesTemplateInvalidWithTheMessageOfTheFirstIssue()
    {
        // Arrange
        const string text = "Order of {{ Name";
        var issues = await Engine.CheckAsync(ALookup(), text, Ct);

        // Act
        var rendered = await Engine.RenderAsync(ALookup(), text, APayload(), Ct);

        // Assert
        Assert.Equal((422, issues.Value[0].Message), (CodeOf(rendered), rendered.Error?.Message));
    }

    [Fact]
    public async Task RenderAsync_DraftWithAnUnknownName_GivesTemplateInvalid()
    {
        // Act
        var rendered = await Engine.RenderAsync(ALookup(), TestData.InEngineSyntax("Order {Unknown}"), APayload(), Ct);

        // Assert
        Assert.Equal(422, CodeOf(rendered));
    }

    [Fact]
    public async Task RenderAsync_DraftOfAPartWithAStoredText_RendersTheDraft()
    {
        // Arrange
        TestData.Seed(Type, "en", Part, TemplatePartKind.PlainText, "Stored {Name}");
        var draft = TestData.InEngineSyntax("Draft {Name}");
        var payload = APayload();

        // Act
        var rendered = await Engine.RenderAsync(ALookup(), draft, payload, Ct);

        // Assert
        Assert.Equal($"Draft {payload.Name}", rendered.Value);
    }
}
