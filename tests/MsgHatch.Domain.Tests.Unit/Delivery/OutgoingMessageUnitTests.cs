using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Delivery;

public sealed class OutgoingMessageUnitTests
{
    [Fact]
    public void Content_MessageWithAnEnvelope_IsTheEnvelope()
    {
        // Arrange
        var envelope = new TestEnvelope("text");
        OutgoingMessage message = TestMessages.AnOutgoingMessage(envelope);

        // Act
        var content = message.Content;

        // Assert
        Assert.Same(envelope, content);
    }
}
