using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class InboxStoreTestsBase<TStore>
{
    [Fact]
    public async Task DeleteTestDataAsync_TestMessagesOfTwoTenants_DeletesOnlyThemAndReturnsTheirCount()
    {
        // Arrange
        var realOfT1 = await AddAsync(AMessage(T1));
        await AddAsync(AMessage(T1, flags: MessageFlags.Test));
        await AddAsync(AMessage(T2, flags: MessageFlags.Test));
        var realOfT2 = await AddAsync(AMessage(T2));

        // Act
        var deleted = await Store.DeleteTestDataAsync(Ct);

        // Assert
        Assert.Equal(2, deleted.Value);
        Assert.Equal([realOfT1, realOfT2], await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task DeleteTestDataAsync_SecondCall_ReturnsZero()
    {
        // Arrange
        await AddAsync(AMessage(T1, flags: MessageFlags.Test));
        await Store.DeleteTestDataAsync(Ct);

        // Act
        var deleted = await Store.DeleteTestDataAsync(Ct);

        // Assert
        Assert.Equal(0, deleted.Value);
    }

    [Fact]
    public async Task DeleteTestDataAsync_DeletedTestMessage_CanBeAddedAgain()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1, flags: MessageFlags.Test));
        await Store.DeleteTestDataAsync(Ct);

        // Act
        await TenantStore(T1).AddAsync(message, Ct);

        // Assert
        Assert.Equal([message], await TestData.GetStoredAsync(Ct));
    }
}
