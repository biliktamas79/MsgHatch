using System.Globalization;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Routing;

public sealed class RegistryOptionsValidatorUnitTests
{
    private const string _refreshInterval = nameof(RegistryOptions.RefreshInterval);

    public static TheoryData<TimeSpan, string[]> RefreshIntervalRows => new()
    {
        { TimeSpan.FromMilliseconds(999), [_refreshInterval] },
        { TimeSpan.FromSeconds(1), [] },
        { TimeSpan.FromMilliseconds(1001), [] },
        { TimeSpan.Parse("00:59:59.999", CultureInfo.InvariantCulture), [] },
        { TimeSpan.FromHours(1), [] },
        { TimeSpan.Parse("01:00:00.001", CultureInfo.InvariantCulture), [_refreshInterval] },
    };

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        // Act
        var result = new RegistryOptionsValidator().Validate(null, new RegistryOptions());

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(RefreshIntervalRows))]
    public void Validate_RefreshInterval_FailsOutsideTheRange(TimeSpan value, string[] expectedFailedProperties)
    {
        // Act
        var result = new RegistryOptionsValidator().Validate(null, new RegistryOptions { RefreshInterval = value });

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
