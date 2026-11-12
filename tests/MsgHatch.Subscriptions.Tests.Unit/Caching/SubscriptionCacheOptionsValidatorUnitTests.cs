using System.Globalization;
using MsgHatch.Subscriptions.Caching;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Caching;

public sealed class SubscriptionCacheOptionsValidatorUnitTests
{
    private const string _expiration = nameof(SubscriptionCacheOptions.Expiration);
    private const string _keyPrefix = nameof(SubscriptionCacheOptions.KeyPrefix);

    public static TheoryData<string, string[]> ExpirationRows => new()
    {
        { "-00:01:00", [_expiration] },
        { "00:00:00", [_expiration] },
        { "00:00:00.999", [_expiration] },
        { "00:00:01", [] },
        { "00:00:01.001", [] },
        { "00:01:00", [] },
        { "23:59:59.999", [] },
        { "1.00:00:00", [] },
        { "1.00:00:00.001", [_expiration] },
    };

    public static TheoryData<string?, string[]> KeyPrefixRows => new()
    {
        { null, [_keyPrefix] },
        { "", [_keyPrefix] },
        { "   ", [_keyPrefix] },
        { "msghatch sub", [_keyPrefix] },
        { "msghatch:süb", [_keyPrefix] },
        { "msghatch:sub/1", [_keyPrefix] },
        { "msghatch:sub", [] },
        { "App_1-x:Y", [] },
    };

    [Theory]
    [MemberData(nameof(ExpirationRows))]
    public void Validate_Expiration_FailsOutsideOneSecondToOneDay(string value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new SubscriptionCacheOptions { Expiration = TimeSpan.Parse(value, CultureInfo.InvariantCulture) };

        // Act
        var result = new SubscriptionCacheOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(KeyPrefixRows))]
    public void Validate_KeyPrefix_FailsWhenItIsMissingOrHasOtherCharactersThanAsciiLettersDigitsColonUnderscoreAndHyphen(
        string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new SubscriptionCacheOptions { KeyPrefix = value! };

        // Act
        var result = new SubscriptionCacheOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
