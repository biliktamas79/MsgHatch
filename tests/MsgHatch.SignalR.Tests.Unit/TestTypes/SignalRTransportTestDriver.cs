using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.Contracts;
using NSubstitute;

namespace MsgHatch.SignalR.Tests.Unit.TestTypes;

/// <summary>
/// The driver of the SignalR transport tests: the client proxy of a hub context substitute as the provider. SignalR
/// fails with an exception, so a failure is the exception that the transport maps to the error type.
/// </summary>
public sealed class SignalRTransportTestDriver : ITransportTestDriver
{
    public HubDoubles Hub { get; } = new();

    public OutgoingMessage AMessage() => SignalRMessages.AnOutgoingMessage();

    public PublishReceipt Succeeds()
    {
        Hub.Proxy.SendCoreAsync(default!, default!, default).ReturnsForAnyArgs(Task.CompletedTask);
        return new PublishReceipt(ProviderMessageId: null);
    }

    public void Fails(ErrorType type) =>
        Hub.Proxy.SendCoreAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromException(ExceptionOf(type)));

    public void WaitsUntilCancelled() =>
        Hub.Proxy.SendCoreAsync(default!, default!, default)
            .ReturnsForAnyArgs(static call => Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, call.ArgAt<CancellationToken>(2)));

    private static Exception ExceptionOf(ErrorType type) =>
        type == Errors.Rejected ? new JsonException()
        : type == Errors.Unavailable ? new IOException()
        : throw new ArgumentOutOfRangeException(nameof(type), type, "The driver fails only with Rejected or Unavailable.");
}
