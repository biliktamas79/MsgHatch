using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.AmazonSES.Tests.Unit;

public sealed class AmazonSesOptionsValidatorUnitTests
{
    private const string _senderEmail = nameof(AmazonSesOptions.SenderEmail);
    private const string _senderName = nameof(AmazonSesOptions.SenderName);
    private const string _region = nameof(AmazonSesOptions.Region);

    public static TheoryData<string?, string[]> SenderEmailRows => new()
    {
        { null, [_senderEmail] },
        { "", [_senderEmail] },
        { "   ", [_senderEmail] },
        { "noreply.example.com", [_senderEmail] },
        { "példa@example.com", [_senderEmail] },
        { "noreply@példa.hu", [_senderEmail] },
        { "noreply@xn--plda-bpa.hu", [] },
        { "Example <noreply@example.com>", [] },
        { "noreply@example.com", [] },
    };

    public static TheoryData<int, string[]> SenderNameRows => new()
    {
        { 99, [] },
        { 100, [] },
        { 101, [_senderName] },
    };

    public static TheoryData<string?, string[]> RegionRows => new()
    {
        { null, [] },
        { "eu-west-1", [] },
        { "EU-WEST-1", [_region] },
    };

    [Theory]
    [MemberData(nameof(SenderEmailRows))]
    public void Validate_SenderEmail_FailsWhenItIsMissingHasNoAtOrIsNotAscii(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new AmazonSesOptions { SenderEmail = value! };

        // Act
        var result = new AmazonSesOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(SenderNameRows))]
    public void Validate_SenderNameLength_FailsAbove100(int length, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new AmazonSesOptions { SenderEmail = "noreply@example.com", SenderName = new string('n', length) };

        // Act
        var result = new AmazonSesOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(RegionRows))]
    public void Validate_Region_FailsWhenItIsNotTheNameOfARegion(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new AmazonSesOptions { SenderEmail = "noreply@example.com", Region = value };

        // Act
        var result = new AmazonSesOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
