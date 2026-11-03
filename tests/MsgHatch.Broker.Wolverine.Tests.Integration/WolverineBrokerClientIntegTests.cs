using MsgHatch.Broker.Wolverine.Tests.Integration.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Broker.Wolverine.Tests.Integration;

/// <summary>
/// The Wolverine broker client against a real RabbitMQ broker, with the required Wolverine setup of section 4.7. The
/// broker comes from <see cref="RabbitMqBroker"/>, and each test is skipped while its URI is empty. The tests check that
/// a receipt means that the broker confirmed the message, and they record the one case where the broker confirms a
/// message and then drops it: a route to a queue that does not exist.
/// </summary>
public sealed class WolverineBrokerClientIntegTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Skip = RabbitMqBroker.NotConfigured, SkipUnless = nameof(RabbitMqBroker.IsConfigured), SkipType = typeof(RabbitMqBroker))]
    public async Task PublishAsync_RouteToAQueue_QueueHoldsTheMessageWithItsMessageIdHeader()
    {
        // Arrange
        var queue = NewName();
        await using var rabbit = await RabbitMqTestHost.StartWithQueueAsync(queue, Ct);
        await rabbit.DeclareExclusiveQueueAsync(queue, Ct);
        var id = Guid.NewGuid().ToString();

        // Act
        var result = await rabbit.Client.PublishAsync(APublish(id), Ct);

        // Assert
        Assert.Null(result.Error);
        Assert.Equal(id, await rabbit.GetHeaderAsync(queue, "msghatch-id", Ct));
    }

    [Fact(Skip = RabbitMqBroker.NotConfigured, SkipUnless = nameof(RabbitMqBroker.IsConfigured), SkipType = typeof(RabbitMqBroker))]
    public async Task PublishAsync_RouteToAQueueThatDoesNotExist_ReturnsAReceiptForAMessageThatTheBrokerDrops()
    {
        // Arrange
        var queue = NewName();
        await using var rabbit = await RabbitMqTestHost.StartWithQueueAsync(queue, Ct);

        // Act
        var result = await rabbit.Client.PublishAsync(APublish(Guid.NewGuid().ToString()), Ct);

        // Assert
        Assert.Null(result.Error);
        Assert.False(await rabbit.QueueExistsAsync(queue, Ct));
    }

    [Fact(Skip = RabbitMqBroker.NotConfigured, SkipUnless = nameof(RabbitMqBroker.IsConfigured), SkipType = typeof(RabbitMqBroker))]
    public async Task PublishAsync_RouteToAnExchangeThatDoesNotExist_ReturnsUnavailable()
    {
        // Arrange
        await using var rabbit = await RabbitMqTestHost.StartWithExchangeAsync(NewName(), Ct);

        // Act
        var result = await rabbit.Client.PublishAsync(APublish(Guid.NewGuid().ToString()), Ct)
            .AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    private static string NewName() => $"msghatch-test-{Guid.NewGuid():N}";

    private static BrokerPublish APublish(string messageId) =>
        new(Guid.NewGuid(), null, new TestPayload("text"), new Dictionary<string, string> { ["msghatch-id"] = messageId });
}
