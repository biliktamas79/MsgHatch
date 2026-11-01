using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
{
    [Fact]
    public async Task DeleteTestDataAsync_TestMessagesOfTwoTenants_DeletesOnlyThemAndReturnsTheirCount()
    {
        // Arrange
        var realOfT1 = AMessage(T1);
        var realOfT2 = AMessage(T2);
        await TenantStore(T1).AddAsync([realOfT1, AMessage(T1, flags: MessageFlags.Test)], Ct);
        await TenantStore(T2).AddAsync([AMessage(T2, flags: MessageFlags.Test), realOfT2], Ct);

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
        await TenantStore(T1).AddAsync([AMessage(T1, flags: MessageFlags.Test)], Ct);
        await Store.DeleteTestDataAsync(Ct);

        // Act
        var deleted = await Store.DeleteTestDataAsync(Ct);

        // Assert
        Assert.Equal(0, deleted.Value);
    }
}
