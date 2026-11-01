using System.Globalization;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

public sealed class DispatchOptionsValidatorUnitTests
{
    private const string _maxDegreeOfParallelism = nameof(DispatchOptions.MaxDegreeOfParallelism);
    private const string _queueCapacity = nameof(DispatchOptions.QueueCapacity);
    private const string _lease = nameof(DispatchOptions.Lease);
    private const string _leaseSafetyMargin = nameof(DispatchOptions.LeaseSafetyMargin);
    private const string _pollInterval = nameof(DispatchOptions.PollInterval);
    private const string _maxPollInterval = nameof(DispatchOptions.MaxPollInterval);

    public static TheoryData<int, string[]> MaxDegreeOfParallelismRows => new()
    {
        { -1, [_maxDegreeOfParallelism] },
        { 0, [_maxDegreeOfParallelism] },
        { 1, [] },
        { 2, [] },
        { 256, [] },
        { 257, [_maxDegreeOfParallelism] },
    };

    public static TheoryData<int, string[]> QueueCapacityRows => new()
    {
        { -1, [_queueCapacity, _queueCapacity] }, // the range, and less than the parallelism of 1
        { 0, [_queueCapacity, _queueCapacity] },
        { 1, [] },
        { 4096, [] },
        { 4097, [_queueCapacity] },
    };

    public static TheoryData<TimeSpan, string[]> LeaseRows => new()
    {
        { Time("00:00:04.999"), [_lease] },
        { TimeSpan.FromSeconds(5), [] },
        { TimeSpan.FromHours(1), [] },
        { Time("01:00:00.001"), [_lease] },
    };

    public static TheoryData<TimeSpan, string[]> LeaseSafetyMarginRows => new()
    {
        { TimeSpan.FromMilliseconds(-1), [_leaseSafetyMargin] },
        { TimeSpan.Zero, [] },
        { TimeSpan.FromMinutes(30), [] },
        { Time("00:30:00.001"), [_leaseSafetyMargin] },
    };

    public static TheoryData<TimeSpan, string[]> PollIntervalRows => new()
    {
        { TimeSpan.FromMilliseconds(9), [_pollInterval] },
        { TimeSpan.FromMilliseconds(10), [] },
        { TimeSpan.FromMinutes(10), [] },
        { Time("00:10:00.001"), [_pollInterval] },
    };

    public static TheoryData<TimeSpan, string[]> MaxPollIntervalRows => new()
    {
        { TimeSpan.FromMilliseconds(10), [] },
        { TimeSpan.FromHours(1), [] },
        { Time("01:00:00.001"), [_maxPollInterval] },
    };

    public static TheoryData<int, int, string[]> QueueCapacityAndParallelismRows => new()
    {
        { 8, 7, [_queueCapacity] },
        { 8, 8, [] },
        { 8, 9, [] },
    };

    public static TheoryData<TimeSpan, TimeSpan, string[]> LeaseAndSafetyMarginRows => new()
    {
        { TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(59), [] },
        { TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), [_leaseSafetyMargin] },
        { TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(61), [_leaseSafetyMargin] },
    };

    public static TheoryData<TimeSpan, TimeSpan, string[]> PollIntervalAndMaxPollIntervalRows => new()
    {
        { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), [_maxPollInterval] },
        { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), [] },
        { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), [] },
    };

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        // Arrange
        var options = new DispatchOptions();

        // Act
        var result = new DispatchOptionsValidator().Validate("Outbox", options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(MaxDegreeOfParallelismRows))]
    public void Validate_MaxDegreeOfParallelism_FailsOutsideTheRange(int value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new DispatchOptions { MaxDegreeOfParallelism = value, QueueCapacity = 4096 };

        // Act
        var result = new DispatchOptionsValidator().Validate("Outbox", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(QueueCapacityRows))]
    public void Validate_QueueCapacity_FailsOutsideTheRange(int value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new DispatchOptions { MaxDegreeOfParallelism = 1, QueueCapacity = value };

        // Act
        var result = new DispatchOptionsValidator().Validate("Outbox", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(LeaseRows))]
    public void Validate_Lease_FailsOutsideTheRange(TimeSpan value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new DispatchOptions { Lease = value, LeaseSafetyMargin = TimeSpan.Zero };

        // Act
        var result = new DispatchOptionsValidator().Validate("Outbox", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(LeaseSafetyMarginRows))]
    public void Validate_LeaseSafetyMargin_FailsOutsideTheRange(TimeSpan value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new DispatchOptions { Lease = TimeSpan.FromHours(1), LeaseSafetyMargin = value };

        // Act
        var result = new DispatchOptionsValidator().Validate("Inbox", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(PollIntervalRows))]
    public void Validate_PollInterval_FailsOutsideTheRange(TimeSpan value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new DispatchOptions { PollInterval = value, MaxPollInterval = TimeSpan.FromHours(1) };

        // Act
        var result = new DispatchOptionsValidator().Validate("Inbox", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(MaxPollIntervalRows))]
    public void Validate_MaxPollInterval_FailsOutsideTheRange(TimeSpan value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new DispatchOptions { PollInterval = TimeSpan.FromMilliseconds(10), MaxPollInterval = value };

        // Act
        var result = new DispatchOptionsValidator().Validate("Inbox", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(QueueCapacityAndParallelismRows))]
    public void Validate_QueueCapacityLessThanTheParallelism_Fails(int parallelism, int queueCapacity, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new DispatchOptions { MaxDegreeOfParallelism = parallelism, QueueCapacity = queueCapacity };

        // Act
        var result = new DispatchOptionsValidator().Validate("Outbox", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(LeaseAndSafetyMarginRows))]
    public void Validate_SafetyMarginThatIsNotLessThanTheLease_Fails(TimeSpan lease, TimeSpan margin, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new DispatchOptions { Lease = lease, LeaseSafetyMargin = margin };

        // Act
        var result = new DispatchOptionsValidator().Validate("Outbox", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(PollIntervalAndMaxPollIntervalRows))]
    public void Validate_MaxPollIntervalLessThanThePollInterval_Fails(TimeSpan poll, TimeSpan maxPoll, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new DispatchOptions { PollInterval = poll, MaxPollInterval = maxPoll };

        // Act
        var result = new DispatchOptionsValidator().Validate("Outbox", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    private static TimeSpan Time(string value) => TimeSpan.Parse(value, CultureInfo.InvariantCulture);
}
