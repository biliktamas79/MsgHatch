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
}
