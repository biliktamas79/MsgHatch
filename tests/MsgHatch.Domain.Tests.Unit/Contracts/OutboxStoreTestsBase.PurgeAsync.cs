using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
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
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    public async Task PurgeAsync_ThreeFinishedMessages_DeletesAtMostMaxCount(int maxCount, int expected)
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
    public async Task PurgeAsync_MaxCountOne_DeletesTheMessageThatFinishedFirst()
    {
        // Arrange
        var later = await AddAsync(Finished(AMessage(T1), DeliveryStatus.Done, Now.AddDays(-1)));
        await AddAsync(Finished(AMessage(T1), DeliveryStatus.Done, Now.AddDays(-2)));

        // Act
        await Store.PurgeAsync(Now, maxCount: 1, Ct);

        // Assert
        Assert.Equal([later.Delivery.Id], (await TestData.GetStoredAsync(Ct)).Select(static message => message.Delivery.Id));
    }

    [Fact]
    public async Task PurgeAsync_SentMessagesOfTwoTenants_DeletesBoth()
    {
        // Arrange
        var ofT1 = await AddAsync(AMessage(T1));
        var ofT2 = await AddAsync(AMessage(T2));
        await Store.RecordTryAsync(ofT1, ATry(ofT1, 1, PublishOutcome.Succeeded), nextTryAtUtc: null, Ct);
        await Store.RecordTryAsync(ofT2, ATry(ofT2, 1, PublishOutcome.Succeeded), nextTryAtUtc: null, Ct);
        Time.Advance(TimeSpan.FromSeconds(2));

        // Act
        var purged = await Store.PurgeAsync(Now, maxCount: 10, Ct);

        // Assert
        Assert.Equal((2, 0), (purged.Value, (await TestData.GetStoredAsync(Ct)).Count));
    }

    [Fact]
    public async Task PurgeAsync_PurgedSentId_IsStoredAgainAsANewMessageWithNoTries()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        await Store.RecordTryAsync(message, ATry(message, 1, PublishOutcome.Succeeded), nextTryAtUtc: null, Ct);
        Time.Advance(TimeSpan.FromSeconds(2));
        await Store.PurgeAsync(Now, maxCount: 10, Ct);

        // Act
        await AddAsync(message);

        // Assert
        Assert.Equal([message], await TestData.GetStoredAsync(Ct));
        Assert.Empty(await TestData.GetTriesAsync(message.Delivery.Id, Ct));
    }
}
