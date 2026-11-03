using System.Text.Json;
using MsgHatch.Broker.Tests.Unit.Contracts;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using NSubstitute;
using Wolverine;

namespace MsgHatch.Broker.Wolverine.Tests.Unit.TestTypes;

/// <summary>
/// The driver of the Wolverine broker client tests: an <see cref="IMessageBus"/> substitute as the library. Wolverine
/// fails with an exception, so a failure is the exception that the client maps to the error type.
/// </summary>
public sealed class WolverineBrokerClientTestDriver : IBrokerClientTestDriver
{
    public IMessageBus Bus { get; } = Substitute.For<IMessageBus>();

    public PublishReceipt Accepts()
    {
        Bus.PublishAsync<object>(default!, default).ReturnsForAnyArgs(ValueTask.CompletedTask);
        return new PublishReceipt(ProviderMessageId: null);
    }

    public void Fails(ErrorType type) =>
        Bus.PublishAsync<object>(default!, default).ReturnsForAnyArgs(ValueTask.FromException(ExceptionOf(type)));

    public void WaitsUntilCancelled() =>
        Bus.PublishAsync<object>(default!, default).ReturnsForAnyArgs(new ValueTask(new TaskCompletionSource().Task));

    public (object Payload, IReadOnlyDictionary<string, string?> Headers, string? Tenant) Published()
    {
        var arguments = Bus.ReceivedCalls().Single().GetArguments();
        var options = (DeliveryOptions)arguments[1]!;
        return (arguments[0]!, options.Headers, options.TenantId);
    }

    private static Exception ExceptionOf(ErrorType type) =>
        type == Errors.Rejected ? new JsonException()
        : type == Errors.Unavailable ? new IOException()
        : throw new ArgumentOutOfRangeException(nameof(type), type, "The driver fails only with Rejected or Unavailable.");
}
