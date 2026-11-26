using MsgHatch.Domain.Failures;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class AdminErrorsUnitTests
{
    public static TheoryData<int, string> FixedTextRows => new()
    {
        { Errors.Rejected.Code, AdminErrors.RejectedText },
        { Errors.Unavailable.Code, AdminErrors.UnavailableText },
        { Errors.Conflict.Code, AdminErrors.ConflictText },
    };

    [Theory]
    [MemberData(nameof(FixedTextRows))]
    public void TextOf_RejectedUnavailableOrConflict_GivesTheFixedTextAndNotTheMessage(int code, string expected)
    {
        // Act
        var text = AdminErrors.TextOf(new Error(Errors.Find(code)!, "The message of the server."));

        // Assert
        Assert.Equal(expected, text);
    }

    [Fact]
    public void TextOf_OtherErrorWithAMessage_GivesTheMessage()
    {
        // Arrange
        const string message = "The part is not a name.";

        // Act
        var text = AdminErrors.TextOf(new Error(Errors.ValidationFailed, message));

        // Assert
        Assert.Equal(message, text);
    }

    [Fact]
    public void TextOf_OtherErrorWithNoMessage_GivesTheNameOfItsType()
    {
        // Act
        var text = AdminErrors.TextOf(new Error(Errors.NotFound));

        // Assert
        Assert.Equal(nameof(Errors.NotFound), text);
    }

    [Fact]
    public void TextOf_NullError_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => AdminErrors.TextOf(null!));
    }
}
