using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class InboxStoreTestsBase<TStore>
{
    [Fact]
    public async Task PurgeAsync_MaxCountZero_ReturnsValidationFailed()
    {
        // Act
        var result = await Store.PurgeAsync(Now, maxCount: 0, Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    public async Task PurgeAsync_MessageFinishedAroundTheTime_DeletesItOnlyWhenItFinishedBefore(int finishedSecondsLater, int expected)
    {
        // Arrange
        await AddAsync(Finished(AMessage(T1), DeliveryStatus.Done, Now.AddSeconds(finishedSecondsLater)));

        // Act
        var purged = await Store.PurgeAsync(Now, maxCount: 10, Ct);

        // Assert
        Assert.Equal((expected, 1 - expected), (purged.Value, (await TestData.GetStoredAsync(Ct)).Count));
    }

    [Theory]
    [InlineData(DeliveryStatus.Done, 1)]
    [InlineData(DeliveryStatus.Failed, 1)]
    [InlineData(DeliveryStatus.Cancelled, 1)]
    [InlineData(DeliveryStatus.Pending, 0)]
    public async Task PurgeAsync_MessageWithAnOldTry_DeletesItOnlyWhenItIsFinished(DeliveryStatus status, int expected)
    {
        // Arrange
        await AddAsync(Finished(AMessage(T1), status, Now.AddDays(-1)));

        // Act
        var purged = await Store.PurgeAsync(Now, maxCount: 10, Ct);

        // Assert
        Assert.Equal(expected, purged.Value);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    public async Task PurgeAsync_CancelledMessageWithNoTry_UsesItsCreationTimeAsTheFinishTime(int createdSecondsLater, int expected)
    {
        // Arrange
        var message = Finished(AMessage(T1), DeliveryStatus.Cancelled, lastTryAtUtc: null);
        await AddAsync(message with { Delivery = message.Delivery with { CreatedAtUtc = Now.AddSeconds(createdSecondsLater) } });

        // Act
        var purged = await Store.PurgeAsync(Now, maxCount: 10, Ct);

        // Assert
        Assert.Equal(expected, purged.Value);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    public async Task PurgeAsync_ThreeFinishedMessagesOfTwoTenants_DeletesAtMostMaxCount(int maxCount, int expected)
    {
        // Arrange
        await AddAsync(Finished(AMessage(T1), DeliveryStatus.Done, Now.AddDays(-1)));
        await AddAsync(Finished(AMessage(T1), DeliveryStatus.Done, Now.AddDays(-1)));
        await AddAsync(Finished(AMessage(T2), DeliveryStatus.Done, Now.AddDays(-1)));

        // Act
        var purged = await Store.PurgeAsync(Now, maxCount, Ct);

        // Assert
        Assert.Equal((expected, 3 - expected), (purged.Value, (await TestData.GetStoredAsync(Ct)).Count));
    }

    [Fact]
    public async Task PurgeAsync_PurgedId_IsStoredAgainAsANewMessage()
    {
        // Arrange
        var message = AMessage(T1);
        await AddAsync(Finished(message, DeliveryStatus.Done, Now.AddDays(-1)));
        await Store.PurgeAsync(Now, maxCount: 10, Ct);

        // Act
        await AddAsync(message);

        // Assert
        Assert.Equal([message], await TestData.GetStoredAsync(Ct));
    }

    protected static ReceivedMessage<TestEvent> Finished(ReceivedMessage<TestEvent> message, DeliveryStatus status, DateTime? lastTryAtUtc) =>
        message with { Delivery = message.Delivery with { Status = status, LastTryAtUtc = lastTryAtUtc } };
}
