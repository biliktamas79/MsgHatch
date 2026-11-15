using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Tests.Unit.Templates;

/// <summary>
/// The template selector that each template engine uses (D205). The fallback tests of D123 are in
/// <c>PlaceholderEmailEngineUnitTests</c>.
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
}
