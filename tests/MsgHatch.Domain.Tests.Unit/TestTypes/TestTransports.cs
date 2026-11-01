using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>Shared by the transport and dispatch tests: transport substitutes of one channel.</summary>
public static class TestTransports
{
    public static ITransport For(ChannelId channel, ErrorOr<PublishReceipt>? result = null)
    {
        var transport = Substitute.For<ITransport>();
        transport.Channel.Returns(channel);
        transport.SendAsync(default!, default, default)
            .ReturnsForAnyArgs(ValueTask.FromResult(result ?? new PublishReceipt("provider-1")));
        return transport;
    }

    /// <summary>A transport whose send completes only when its token is cancelled. <paramref name="sendStarted"/> completes at the first send.</summary>
    public static ITransport NeverCompleting(ChannelId channel, TaskCompletionSource sendStarted)
    {
        var transport = Substitute.For<ITransport>();
        transport.Channel.Returns(channel);
        transport.SendAsync(default!, default, default)
            .ReturnsForAnyArgs(call => new ValueTask<ErrorOr<PublishReceipt>>(WaitUntilCancelledAsync(sendStarted, call.ArgAt<CancellationToken>(2))));
        return transport;
    }

    private static async Task<ErrorOr<PublishReceipt>> WaitUntilCancelledAsync(TaskCompletionSource sendStarted, CancellationToken ct)
    {
        sendStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, ct);
        return new Error(Errors.OperationCancelled);
    }
}
