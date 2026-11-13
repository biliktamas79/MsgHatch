namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class InboxStoreTestsBase<TStore>
{
    [Fact]
    public async Task GetAsync_StoredMessage_GivesItWithItsEvent()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1, text: "Ősz-űr \"quoted\" <tag> & more"));

        // Act
        var stored = await TenantStore(T1).GetAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal(message, stored.Value);
    }

    [Fact]
    public async Task GetAsync_UnknownId_GivesNull()
    {
        // Arrange
        await AddAsync(AMessage(T1));

        // Act
        var stored = await TenantStore(T1).GetAsync(Guid.NewGuid(), Ct);

        // Assert
        Assert.Equal((false, null), (stored.IsError, stored.Value));
    }

    [Fact]
    public async Task GetAsync_SameIdOfAnotherTenant_GivesOnlyTheMessageOfItsTenant()
    {
        // Arrange
        var id = Guid.NewGuid();
        await AddAsync(AMessage(T2, text: "of t2", id: id));
        var ofT1 = await AddAsync(AMessage(T1, text: "of t1", id: id));

        // Act
        var stored = await TenantStore(T1).GetAsync(id, Ct);

        // Assert
        Assert.Equal(ofT1, stored.Value);
    }

    [Fact]
    public async Task GetAsync_IdOfAnotherTenant_GivesNull()
    {
        // Arrange
        var message = await AddAsync(AMessage(T2));

        // Act
        var stored = await TenantStore(T1).GetAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal((false, null), (stored.IsError, stored.Value));
    }
}
