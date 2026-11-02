using System.Globalization;

namespace MsgHatch.Email.Tests.Unit.Contracts;

public abstract partial class EmailTemplateEngineTestsBase<TEngine>
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("hu-HU")]
    public async Task RenderAsync_PayloadValues_RenderInTheCultureOfTheRequest(string language)
    {
        // Arrange
        var culture = CultureInfo.GetCultureInfo(language);
        TestData.Seed(Type, language, "Order {Count} of {Name}", "<p>{Total} on {ShippedAt}</p>", "{Total}");

        // Act
        var rendered = await RenderAsync(APayload(name: "Client 1", count: 3, total: 1234.5m), language);

        // Assert
        Assert.Equal(
            new RenderedEmail("Order 3 of Client 1", $"<p>{1234.5m.ToString(culture)} on {ShippedAt.ToString(culture)}</p>", 1234.5m.ToString(culture)),
            rendered);
    }

    [Fact]
    public async Task RenderAsync_ValueWithAScriptTag_IsEncodedInTheHtmlPartAndNotInTheOtherParts()
    {
        // Arrange
        TestData.Seed(Type, "en", "{Name}", "<p>{Name}</p>", "{Name}");

        // Act
        var rendered = await RenderAsync(APayload(name: "<script>alert(1)</script>"), "en");

        // Assert
        Assert.Equal(
            new RenderedEmail("<script>alert(1)</script>", "<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>", "<script>alert(1)</script>"),
            rendered);
    }

    [Fact]
    public async Task RenderAsync_AccentedCharacters_RenderUnchangedInEachPart()
    {
        // Arrange
        TestData.Seed(Type, "hu", "{Name} rendelése", "<p>Kedves {Name}, ő ű</p>", "Kedves {Name}, ő ű");

        // Act
        var rendered = await RenderAsync(APayload(name: "Ödön Űr"), "hu");

        // Assert
        Assert.Equal(new RenderedEmail("Ödön Űr rendelése", "<p>Kedves Ödön Űr, ő ű</p>", "Kedves Ödön Űr, ő ű"), rendered);
    }

    [Fact]
    public async Task RenderAsync_TemplateWithNoTextPart_GivesNoTextBody()
    {
        // Arrange
        TestData.Seed(Type, "en", "Order", "<p>{Name}</p>", null);

        // Act
        var rendered = await RenderAsync(APayload(name: "Client 1"), "en");

        // Assert
        Assert.Equal(new RenderedEmail("Order", "<p>Client 1</p>", null), rendered);
    }
}
