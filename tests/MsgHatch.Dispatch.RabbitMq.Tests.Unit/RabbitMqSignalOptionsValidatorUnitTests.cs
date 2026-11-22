using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Dispatch.RabbitMq.Tests.Unit;

public sealed class RabbitMqSignalOptionsValidatorUnitTests
{
    private const string _uri = nameof(RabbitMqSignalOptions.Uri);
    private const string _exchange = nameof(RabbitMqSignalOptions.ExchangeName);

    public static TheoryData<string?, string[]> UriRows => new()
    {
        { null, [_uri] },
        { "amqp://localhost", [] },
        { "amqps://user:password@rabbit.example.com:5671/vhost", [] },
        { "http://localhost:5672", [_uri] },
        { "rabbit.example.com", [_uri] },
    };

    public static TheoryData<string?, string[]> ExchangeNameRows => new()
    {
        { null, [_exchange] },
        { "", [_exchange] },
        { "msghatch.dispatch", [] },
        { "Msg_Hatch-9:signal.v2", [] },
        { new string('a', 255), [] },
        { new string('a', 256), [_exchange] },
        { "amq.dispatch", [_exchange] },
        { "msghatch dispatch", [_exchange] },
        { "msghatch/dispatch", [_exchange] },
    };

    [Theory]
    [MemberData(nameof(UriRows))]
    public void Validate_Uri_FailsWhenItIsMissingRelativeOrNotAmqp(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new RabbitMqSignalOptions { Uri = value is null ? null : new Uri(value, UriKind.RelativeOrAbsolute) };

        // Act
        var result = new RabbitMqSignalOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(ExchangeNameRows))]
    public void Validate_ExchangeName_FailsWhenItIsMissingTooLongReservedOrHasAnotherCharacter(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new RabbitMqSignalOptions { Uri = new Uri("amqp://localhost"), ExchangeName = value! };

        // Act
        var result = new RabbitMqSignalOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
