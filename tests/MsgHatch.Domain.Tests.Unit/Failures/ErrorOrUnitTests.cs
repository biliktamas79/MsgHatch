using System.Globalization;
using MsgHatch.Domain.Failures;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Failures;

public sealed class ErrorOrUnitTests
{
    [Fact]
    public void ForValue_Value_IsSuccessWithTheValue()
    {
        // Arrange
        const int value = 42;

        // Act
        var result = ErrorOr<int>.ForValue(value);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsError);
        Assert.Equal(value, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ImplicitConversion_Value_IsSuccessWithTheValue()
    {
        // Arrange
        const string value = "text";

        // Act
        ErrorOr<string> result = value;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value);
    }

    [Fact]
    public void ForError_Error_IsErrorWithTheSameError()
    {
        // Arrange
        var error = new Error(Errors.NotFound);

        // Act
        var result = ErrorOr<int>.ForError(error);

        // Assert
        Assert.True(result.IsError);
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ImplicitConversion_Error_IsErrorWithTheSameError()
    {
        // Arrange
        var error = new Error(Errors.Conflict);

        // Act
        ErrorOr<int> result = error;

        // Assert
        Assert.True(result.IsError);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ThenIfSuccess_Value_ReturnsTheResultOfTheFunction()
    {
        // Arrange
        ErrorOr<int> result = 41;

        // Act
        var next = result.ThenIfSuccess(static v => ErrorOr<string>.ForValue((v + 1).ToString(CultureInfo.InvariantCulture)));

        // Assert
        Assert.True(next.IsSuccess);
        Assert.Equal("42", next.Value);
    }

    [Fact]
    public void ThenIfSuccess_Error_ReturnsTheSameErrorAndDoesNotCallTheFunction()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        ErrorOr<int> result = error;
        var onSuccess = Substitute.For<Func<int, ErrorOr<string>>>();

        // Act
        var next = result.ThenIfSuccess(onSuccess);

        // Assert
        Assert.Same(error, next.Error);
        onSuccess.DidNotReceiveWithAnyArgs().Invoke(default);
    }

    [Fact]
    public void Default_Value_IsErrorWithUninitialized()
    {
        // Arrange
        var result = default(ErrorOr<int>);

        // Act
        var error = result.Error;

        // Assert
        Assert.True(result.IsError);
        Assert.Equal(Errors.Uninitialized, error?.Type);
    }

    [Fact]
    public void ThenIfSuccess_ValueResult_AllocatesZeroBytes()
    {
        // Arrange
        Func<int, ErrorOr<int>> increment = static v => ErrorOr<int>.ForValue(v + 1);
        _ = ErrorOr<int>.ForValue(0).ThenIfSuccess(increment);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        ErrorOr<int> result = 41;
        var next = result.ThenIfSuccess(increment);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.Equal(42, next.Value);
        Assert.Equal(0, allocated);
    }
}
