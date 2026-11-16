using MsgHatch.Domain.Identity;
using MsgHatch.Templates;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Tests.Unit;

/// <summary>
/// The template selector that each template engine uses (D205). The fallback tests of D123 are in
/// <c>PlaceholderTemplateEngineUnitTests</c>.
/// </summary>
public sealed class TemplateSelectorUnitTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_DefaultLanguageThatIsNullEmptyOrWhiteSpace_ThrowsArgumentException(string? defaultLanguage)
    {
        // Act
        var exception = Record.Exception(() => new TemplateSelector(tenantStore: null, sharedStore: null, defaultLanguage!));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Fact]
    public async Task FindAsync_CompanyFolderWithOnlyASubject_GivesTheSubjectOfTheCompanyAndTheBodyOfTheDefaultFolder()
    {
        // Arrange
        var store = new EmbeddedResourceTemplateStore(typeof(TemplateSelectorUnitTests).Assembly, EmbeddedTemplateFiles.Prefix);
        var selector = new TemplateSelector(tenantStore: null, store, "en");
        var type = new MessageType("TestEvent");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var subject = await selector.FindAsync(new TemplateRequest(type, "en", "42", "subject", TemplatePartKind.PlainText), ct);
        var body = await selector.FindAsync(new TemplateRequest(type, "en", "42", "body", TemplatePartKind.Html), ct);

        // Assert
        Assert.Equal(("Company 42 order {{OrderNumber}}", "<p>Hello {{Name}}</p>"), (subject.Value.Text, body.Value.Text));
    }

    [Fact]
    public async Task FindAsync_CompanyFolderWithASubjectAndABody_GivesBothPartsOfTheCompany()
    {
        // Arrange
        var store = new EmbeddedResourceTemplateStore(typeof(TemplateSelectorUnitTests).Assembly, EmbeddedTemplateFiles.Prefix);
        var selector = new TemplateSelector(tenantStore: null, store, "en");
        var type = new MessageType("TestEvent");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var subject = await selector.FindAsync(new TemplateRequest(type, "en", "acme", "subject", TemplatePartKind.PlainText), ct);
        var body = await selector.FindAsync(new TemplateRequest(type, "en", "acme", "body", TemplatePartKind.Html), ct);

        // Assert
        Assert.Equal(("Acme order {{OrderNumber}}", "<p>Acme {{Name}}</p>"), (subject.Value.Text, body.Value.Text));
    }

    [Fact]
    public async Task FindAsync_PartWithNoTranslation_GivesThePartOfTheDefaultLanguage()
    {
        // Arrange
        var store = new EmbeddedResourceTemplateStore(typeof(TemplateSelectorUnitTests).Assembly, EmbeddedTemplateFiles.Prefix);
        var selector = new TemplateSelector(tenantStore: null, store, "en");
        var type = new MessageType("OrderShipped");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var subject = await selector.FindAsync(new TemplateRequest(type, "hu", Scope: null, "subject", TemplatePartKind.PlainText), ct);
        var text = await selector.FindAsync(new TemplateRequest(type, "hu", Scope: null, "body", TemplatePartKind.PlainText), ct);

        // Assert
        Assert.Equal((OrderShippedText("hu", "subject.txt"), OrderShippedText("en", "body.txt")), (subject.Value.Text, text.Value.Text));
    }

    private static string OrderShippedText(string language, string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TemplateFiles", $"OrderShipped.{language}.{file}"));
}
