using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

public sealed class MsgHatchOptionsValidatorUnitTests
{
    private const string _defaultLanguage = nameof(MsgHatchOptions.DefaultLanguage);

    public static TheoryData<string?, string[]> DefaultLanguageRows => new()
    {
        { (string?)null, [_defaultLanguage] },
        { "", [_defaultLanguage] },
        { "  ", [_defaultLanguage] },
        { "en", [] },
        { "hu-HU", [] },
        { "xx-YY", [_defaultLanguage] }, // not a known culture
        { "Ősz", [_defaultLanguage] }, // accented characters
    };

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        // Arrange
        var options = new MsgHatchOptions();

        // Act
        var result = new MsgHatchOptionsValidator().Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(DefaultLanguageRows))]
    public void Validate_DefaultLanguage_FailsForAMissingOrUnknownCulture(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new MsgHatchOptions { DefaultLanguage = value! };

        // Act
        var result = new MsgHatchOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
