using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Resend.Tests.Unit;

public sealed class ResendOptionsValidatorUnitTests
{
    private const string _apiKey = nameof(ResendOptions.ApiKey);
    private const string _senderEmail = nameof(ResendOptions.SenderEmail);
    private const string _senderName = nameof(ResendOptions.SenderName);

    public static TheoryData<string?, string[]> ApiKeyRows => new()
    {
        { null, [_apiKey] },
        { "", [_apiKey] },
        { "   ", [_apiKey] },
        { "re_123", [] },
    };

    public static TheoryData<string?, string[]> SenderEmailRows => new()
    {
        { null, [_senderEmail] },
        { "", [_senderEmail] },
        { "   ", [_senderEmail] },
        { "noreply.example.com", [_senderEmail] },
        { "noreply@example.com", [] },
        { "Example <noreply@example.com>", [] },
    };

    public static TheoryData<int, string[]> SenderNameRows => new()
    {
        { 99, [] },
        { 100, [] },
        { 101, [_senderName] },
    };

    [Theory]
    [MemberData(nameof(ApiKeyRows))]
    public void Validate_ApiKey_FailsWhenItIsMissing(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = AnOptions(apiKey: value!);

        // Act
        var result = new ResendOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(SenderEmailRows))]
    public void Validate_SenderEmail_FailsWhenItIsMissingOrHasNoAt(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = AnOptions(senderEmail: value!);

        // Act
        var result = new ResendOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(SenderNameRows))]
    public void Validate_SenderNameLength_FailsAbove100(int length, string[] expectedFailedProperties)
    {
        // Arrange
        var options = AnOptions(senderName: new string('n', length));

        // Act
        var result = new ResendOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    private static ResendOptions AnOptions(string apiKey = "re_123", string senderEmail = "noreply@example.com", string? senderName = null) =>
        new() { ApiKey = apiKey, SenderEmail = senderEmail, SenderName = senderName };
}
