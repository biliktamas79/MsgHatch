using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Dispatch.NamedPipes.Tests.Unit;

public sealed class NamedPipeSignalOptionsValidatorUnitTests
{
    private const string _prefix = nameof(NamedPipeSignalOptions.PipeNamePrefix);

    public static TheoryData<string?, string[]> PrefixRows => new()
    {
        { null, [_prefix] },
        { "", [_prefix] },
        { new string('a', 40), [] },
        { new string('a', 41), [_prefix] },
        { "a.b", [_prefix] },
        { "a/b", [_prefix] },
        { "Msg_Hatch-9", [] },
    };

    [Theory]
    [MemberData(nameof(PrefixRows))]
    public void Validate_PipeNamePrefix_FailsWhenItIsMissingTooLongOrHasAnotherCharacter(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new NamedPipeSignalOptions { PipeNamePrefix = value! };

        // Act
        var result = new NamedPipeSignalOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
