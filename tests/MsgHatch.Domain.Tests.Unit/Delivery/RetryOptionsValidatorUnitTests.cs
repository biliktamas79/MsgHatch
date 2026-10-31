using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Delivery;

public sealed class RetryOptionsValidatorUnitTests
{
    private const string _maxTryCount = nameof(RetryOptions.MaxTryCount);
    private const string _baseDelay = nameof(RetryOptions.BaseDelay);
    private const string _maxDelay = nameof(RetryOptions.MaxDelay);
    private const string _expiry = nameof(RetryOptions.Expiry);

    public static TheoryData<int, string[]> MaxTryCountRows => new()
    {
        { -1, [_maxTryCount] },
        { 0, [_maxTryCount] },
        { 1, [] },
        { 2, [] },
        { 99, [] },
        { 100, [] },
        { 101, [_maxTryCount] },
    };

    public static TheoryData<TimeSpan, string[]> BaseDelayRows => new()
    {
        { TimeSpan.FromSeconds(-1), [_baseDelay] },
        { TimeSpan.Zero, [_baseDelay] },
        { TimeSpan.FromMilliseconds(1), [] },
        { TimeSpan.FromMilliseconds(2), [] },
        { TimeSpan.Parse("00:59:59.999", System.Globalization.CultureInfo.InvariantCulture), [] },
        { TimeSpan.FromHours(1), [] },
        { TimeSpan.Parse("01:00:00.001", System.Globalization.CultureInfo.InvariantCulture), [_baseDelay] },
    };

    public static TheoryData<TimeSpan, string[]> MaxDelayRows => new()
    {
        { TimeSpan.FromMilliseconds(999), [_maxDelay] },
        { TimeSpan.FromSeconds(1), [] },
        { TimeSpan.FromSeconds(2), [] },
        { TimeSpan.FromDays(7), [] },
        { TimeSpan.Parse("7.00:00:00.001", System.Globalization.CultureInfo.InvariantCulture), [_maxDelay] },
    };

    public static TheoryData<TimeSpan?, string[]> ExpiryRows => new()
    {
        { (TimeSpan?)null, [] },
        { TimeSpan.FromMilliseconds(999), [_expiry] },
        { TimeSpan.FromSeconds(1), [] },
        { TimeSpan.FromDays(30), [] },
        { TimeSpan.Parse("30.00:00:00.001", System.Globalization.CultureInfo.InvariantCulture), [_expiry] },
    };

    public static TheoryData<TimeSpan, TimeSpan, string[]> BaseDelayAndMaxDelayRows => new()
    {
        { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), [_maxDelay] },
        { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), [] },
        { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), [] },
    };

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        // Arrange
        var options = new RetryOptions();

        // Act
        var result = new RetryOptionsValidator().Validate("Test:TestEnvelope", options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(MaxTryCountRows))]
    public void Validate_MaxTryCount_FailsOutsideTheRange(int value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new RetryOptions { MaxTryCount = value };

        // Act
        var result = new RetryOptionsValidator().Validate("Test:TestEnvelope", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(BaseDelayRows))]
    public void Validate_BaseDelay_FailsOutsideTheRange(TimeSpan value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new RetryOptions { BaseDelay = value, MaxDelay = TimeSpan.FromDays(7) };

        // Act
        var result = new RetryOptionsValidator().Validate("Test:TestEnvelope", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(MaxDelayRows))]
    public void Validate_MaxDelay_FailsOutsideTheRange(TimeSpan value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new RetryOptions { BaseDelay = TimeSpan.FromMilliseconds(1), MaxDelay = value };

        // Act
        var result = new RetryOptionsValidator().Validate("Test:TestEnvelope", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(ExpiryRows))]
    public void Validate_Expiry_FailsOutsideTheRange(TimeSpan? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new RetryOptions { Expiry = value };

        // Act
        var result = new RetryOptionsValidator().Validate("Test:TestEnvelope", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(BaseDelayAndMaxDelayRows))]
    public void Validate_BaseDelayGreaterThanMaxDelay_FailsOnMaxDelay(
        TimeSpan baseDelay, TimeSpan maxDelay, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new RetryOptions { BaseDelay = baseDelay, MaxDelay = maxDelay };

        // Act
        var result = new RetryOptionsValidator().Validate("Test:TestEnvelope", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
