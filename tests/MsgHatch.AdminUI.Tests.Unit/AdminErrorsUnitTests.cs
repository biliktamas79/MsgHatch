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
        var text = AdminErrors.TextOf(new Error(Errors.Find(code)!, "The message of the server."));

        Assert.Equal(expected, text);
    }

    [Fact]
    public void TextOf_OtherErrorWithAMessage_GivesTheMessage()
    {
        var text = AdminErrors.TextOf(new Error(Errors.ValidationFailed, "The part is not a name."));

        Assert.Equal("The part is not a name.", text);
    }

    [Fact]
    public void TextOf_OtherErrorWithNoMessage_GivesTheNameOfItsType()
    {
        var text = AdminErrors.TextOf(new Error(Errors.NotFound));

        Assert.Equal(nameof(Errors.NotFound), text);
    }

    [Fact]
    public void TextOf_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AdminErrors.TextOf(null!));
    }
}
