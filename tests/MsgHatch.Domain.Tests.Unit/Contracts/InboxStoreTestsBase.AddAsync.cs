using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class InboxStoreTestsBase<TStore>
{
    private static Dictionary<string, TenantKey> Keys => new() { [""] = TenantKey.None, ["t1"] = T1, ["t2"] = T2 };

    [Fact]
    public async Task AddAsync_OneMessage_StoresItUnchanged()
    {
        // Arrange
        var message = AMessage(T1, text: "Ősz-űr \"quoted\" back\\slash <tag> & more", flags: MessageFlags.Test);

        // Act
        var error = await TenantStore(T1).AddAsync(message, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal([message], await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task AddAsync_SeveralMessages_StoresEachMessage()
    {
        // Arrange
        ReceivedMessage[] messages = [AMessage(T1, text: "first"), AMessage(T1, text: "second"), AMessage(T1, text: "third")];

        // Act
        await TenantStore(T1).AddAsync(messages[0], Ct);
        await TenantStore(T1).AddAsync(messages[1], Ct);
        await TenantStore(T1).AddAsync(messages[2], Ct);

        // Assert
        Assert.Equal(messages, await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task AddAsync_IdThatIsAlreadyStoredForTheTenant_KeepsTheStoredMessage()
    {
        // Arrange
        var stored = await AddAsync(AMessage(T1, text: "stored"));
        var again = AMessage(T1, text: "again", id: stored.Delivery.Id);

        // Act
        var error = await TenantStore(T1).AddAsync(again, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal([stored], await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task AddAsync_SameIdWithTwoTenants_StoresTwoMessages()
    {
        // Arrange
        var ofT1 = await AddAsync(AMessage(T1));
        var ofT2 = AMessage(T2, id: ofT1.Delivery.Id);

        // Act
        await TenantStore(T2).AddAsync(ofT2, Ct);

        // Assert
        Assert.Equal([ofT1, ofT2], await TestData.GetStoredAsync(Ct));
    }

    [Theory]
    [MemberData(nameof(OutboxStoreTestRows.OtherTenantRows), MemberType = typeof(OutboxStoreTestRows))]
    public async Task AddAsync_MessageOfAnotherTenant_ReturnsValidationFailedAndAddsNothing(string storeTenant, string messageTenant)
    {
        // Arrange
        var message = AMessage(Keys[messageTenant]);

        // Act
        var error = await TenantStore(Keys[storeTenant]).AddAsync(message, Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, error?.Type);
        Assert.Empty(await TestData.GetStoredAsync(Ct));
    }
}
