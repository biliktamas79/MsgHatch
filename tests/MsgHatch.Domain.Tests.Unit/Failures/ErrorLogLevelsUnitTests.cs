using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Tests.Unit.Failures;

/// <summary>The log level of a failed call of a logging wrapper (section 4.15).</summary>
public sealed class ErrorLogLevelsUnitTests
{
    [Theory]
    [InlineData(460, "OperationCancelled", true, LogLevel.Information)]
    [InlineData(503, "Unavailable", true, LogLevel.Warning)]
    [InlineData(404, "NotFound", false, LogLevel.Error)]
    public void For_ErrorType_GivesTheLevelOfTheDesign(int code, string name, bool isTransient, LogLevel expected)
    {
        // Arrange
        var error = new Error(new ErrorType(code, name, isTransient));

        // Act
        var level = ErrorLogLevels.For(error);

        // Assert
        Assert.Equal(expected, level);
    }
}
