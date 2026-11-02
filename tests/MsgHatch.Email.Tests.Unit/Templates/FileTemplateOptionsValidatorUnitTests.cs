using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Tests.Unit.Templates;

public sealed class FileTemplateOptionsValidatorUnitTests
{
    private const string _rootPath = nameof(FileTemplateOptions.RootPath);

    public static TheoryData<string?, string[]> RootPathRows => new()
    {
        { null, [_rootPath] },
        { "", [_rootPath] },
        { "   ", [_rootPath] },
        { "templates", [] },
        { "C:\\Ödön\\sablonok & <x>", [] },
    };

    [Theory]
    [MemberData(nameof(RootPathRows))]
    public void Validate_RootPath_FailsWhenItIsMissing(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new FileTemplateOptions { RootPath = value! };

        // Act
        var result = new FileTemplateOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
