using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.FileSystem.Tests.Unit;

public sealed class FileStoreOptionsValidatorUnitTests
{
    private const string _rootPath = nameof(FileStoreOptions.RootPath);

    public static TheoryData<string?, string[]> RootPathRows => new()
    {
        { null, [_rootPath] },
        { "", [_rootPath] },
        { "   ", [_rootPath] },
        { "data", [] },
        { "C:\\Ödön\\üzenetek & <x>", [] },
    };

    [Theory]
    [MemberData(nameof(RootPathRows))]
    public void Validate_RootPath_FailsWhenItIsMissing(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new FileStoreOptions { RootPath = value! };

        // Act
        var result = new FileStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
