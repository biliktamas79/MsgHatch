using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.SignalR.Tests.Unit.TestTypes;

namespace MsgHatch.SignalR.Tests.Unit;

/// <summary>The golden file of the SignalR envelope (section 4.13), read with the content type of a SignalR registration.</summary>
public sealed class MsgHatchJsonUnitTests
{
    [Fact]
    public async Task DeserializeOutgoing_SignalRSampleOfVersion1_ReadsTheSignalREnvelope()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestEvent();
        await using var provider = services.Build();

        // Act
        var message = provider.GetRequiredService<MsgHatchJson>().DeserializeOutgoing(Sample("OutgoingSignalRMessage.v1.json"));

        // Assert
        Assert.Equal(
            new SignalREnvelope<TestPayload>(new TestPayload("Order 1001 is shipped")),
            Assert.IsType<OutgoingMessage<SignalREnvelope<TestPayload>>>(message.Value).Envelope);
    }

    private static byte[] Sample(string fileName)
    {
        using var stream = typeof(MsgHatchJsonUnitTests).Assembly.GetManifestResourceStream($"MsgHatch.SignalR.Tests.Unit.Samples.{fileName}")!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
