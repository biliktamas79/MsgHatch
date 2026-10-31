using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Delivery;

public sealed class OutgoingMessageUnitTests
{
    [Fact]
    public void Content_MessageWithAnEnvelope_IsTheEnvelope()
    {
        // Arrange
        var envelope = new TestEnvelope("text");
        OutgoingMessage message = AnOutgoingMessage(envelope);

        // Act
        var content = message.Content;

        // Assert
        Assert.Same(envelope, content);
    }

    private static OutgoingMessage<TestEnvelope> AnOutgoingMessage(TestEnvelope envelope)
    {
        var createdAtUtc = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var delivery = new DeliveryState(
            Guid.NewGuid(), DeliveryStatus.Pending, TryCount: 0, MaxTryCount: 8, createdAtUtc, LastTryAtUtc: null,
            NextTryAtUtc: createdAtUtc, LeaseUntilUtc: null, ExpiresAtUtc: null, LastError: null);
        return new OutgoingMessage<TestEnvelope>(
            SchemaVersion: 1, delivery, SourceMessageId: Guid.NewGuid(), Tenant: null, new MessageType("TestEvent"),
            new ChannelId("Test"), new DeliveryTarget(DeliveryTargetKind.User, "u1"), "Test:TestEnvelope",
            ContentVersion: 1, MessageFlags.None, TraceParent: null, Trace: null, envelope);
    }
}
