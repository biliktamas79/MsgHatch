using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.Contracts;
using NSubstitute;

namespace MsgHatch.Broker.Tests.Unit.TestTypes;

/// <summary>The driver of the broker transport tests: a broker client substitute as the provider.</summary>
public sealed class BrokerTransportTestDriver : ITransportTestDriver
{
    public IBrokerClient Client { get; } = Substitute.For<IBrokerClient>();

    public OutgoingMessage AMessage() => BrokerMessages.AnOutgoingMessage();

    public PublishReceipt Succeeds()
    {
        var receipt = new PublishReceipt("broker-1");
        Client.PublishAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<PublishReceipt>>(receipt));
        return receipt;
    }

    public void Fails(ErrorType type) =>
        Client.PublishAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(type)));

    public void WaitsUntilCancelled() =>
        Client.PublishAsync(default, default)
            .ReturnsForAnyArgs(static call => new ValueTask<ErrorOr<PublishReceipt>>(WaitUntilCancelledAsync(call.ArgAt<CancellationToken>(1))));

    private static async Task<ErrorOr<PublishReceipt>> WaitUntilCancelledAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return new Error(Errors.OperationCancelled);
    }
}
