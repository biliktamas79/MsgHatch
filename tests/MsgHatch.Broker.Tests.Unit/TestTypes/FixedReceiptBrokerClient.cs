using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Broker.Tests.Unit.TestTypes;

/// <summary>A broker client that the DI container creates, for the setup method with a client type.</summary>
public sealed class FixedReceiptBrokerClient : IBrokerClient
{
    public static readonly PublishReceipt Receipt = new("fixed-1");

    public ValueTask<ErrorOr<PublishReceipt>> PublishAsync(BrokerPublish publish, CancellationToken ct) =>
        ValueTask.FromResult<ErrorOr<PublishReceipt>>(Receipt);

    public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => ValueTask.FromResult<Error?>(null);
}
