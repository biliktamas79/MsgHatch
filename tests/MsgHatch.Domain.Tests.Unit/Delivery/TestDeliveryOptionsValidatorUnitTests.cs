using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Delivery;

public sealed class TestDeliveryOptionsValidatorUnitTests
{
    private const string _mode = nameof(TestDeliveryOptions.Mode);
    private const string _redirectTo = nameof(TestDeliveryOptions.RedirectTo);

    public static TheoryData<TestDeliveryMode, string?, string[]> ModeRows => new()
    {
        { TestDeliveryMode.Suppress, null, [] },
        { TestDeliveryMode.Deliver, null, [] },
        { TestDeliveryMode.Redirect, "tester@example.com", [] },
        { (TestDeliveryMode)3, null, [_mode] }, // not a defined mode
    };

    public static TheoryData<string?, string[]> RedirectToRows => new()
    {
        { null, [_redirectTo] },
        { "", [_redirectTo] },
        { "  ", [_redirectTo] },
        { "Ősz-űr test group", [] },
        { new string('a', 255), [] },
        { new string('a', 256), [] },
        { new string('a', 257), [_redirectTo] },
    };

    [Fact]
    public void Validate_Defaults_SucceedWithTheModeSuppress()
    {
        // Arrange
        var options = new TestDeliveryOptions();

        // Act
        var result = new TestDeliveryOptionsValidator().Validate("Test", options);

        // Assert
        Assert.Equal((true, TestDeliveryMode.Suppress), (result.Succeeded, options.Mode));
    }

    [Theory]
    [MemberData(nameof(ModeRows))]
    public void Validate_Mode_FailsForAModeThatIsNotDefined(TestDeliveryMode mode, string? redirectTo, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new TestDeliveryOptions { Mode = mode, RedirectTo = redirectTo };

        // Act
        var result = new TestDeliveryOptionsValidator().Validate("Test", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(RedirectToRows))]
    public void Validate_RedirectToOfTheModeRedirect_FailsWhenMissingOrTooLong(string? redirectTo, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new TestDeliveryOptions { Mode = TestDeliveryMode.Redirect, RedirectTo = redirectTo };

        // Act
        var result = new TestDeliveryOptionsValidator().Validate("Test", options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Fact]
    public async Task ConfigureTestDelivery_ValueInCodeAndInTheSectionOfTheChannel_TheValueInCodeWins()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["MsgHatch:TestDelivery:Test:Mode"] = "Deliver",
            ["MsgHatch:TestDelivery:Test:RedirectTo"] = "tester@example.com",
        });
        services.AddMsgHatch().ConfigureTestDelivery(TestChannel.Id, static o => o.Mode = TestDeliveryMode.Redirect).AddTestChannel();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptionsMonitor<TestDeliveryOptions>>().Get("Test");

        // Assert
        Assert.Equal((TestDeliveryMode.Redirect, "tester@example.com"), (options.Mode, options.RedirectTo));
    }

    [Fact]
    public async Task ConfigureTestDelivery_RedirectWithNoRedirectTo_FailsTheStartupValidation()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores().UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .ConfigureTestDelivery(TestChannel.Id, static o => o.Mode = TestDeliveryMode.Redirect);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }
}
