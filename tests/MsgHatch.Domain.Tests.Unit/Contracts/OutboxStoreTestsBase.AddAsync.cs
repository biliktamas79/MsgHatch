using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
{
    private static Dictionary<string, TenantKey> Keys => new() { [""] = TenantKey.None, ["t1"] = T1, ["t2"] = T2 };

    [Fact]
    public async Task AddAsync_EmptyBatch_StoresNothing()
    {
        // Act
        var error = await TenantStore(TenantKey.None).AddAsync([], Ct);

        // Assert
        Assert.Null(error);
        Assert.Empty(await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task AddAsync_OneMessage_StoresItUnchanged()
    {
        // Arrange
        var message = AMessage(T1, text: "Ősz-űr \"quoted\" back\\slash <tag> & more", flags: MessageFlags.Test);

        // Act
        var error = await TenantStore(T1).AddAsync([message], Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal([message], await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task AddAsync_SeveralMessages_StoresEachMessage()
    {
        // Arrange
        OutgoingMessage[] messages = [AMessage(T1, text: "first"), AMessage(T1, text: "second"), AMessage(T1, text: "third")];

        // Act
        await TenantStore(T1).AddAsync(messages, Ct);

        // Assert
        Assert.Equal(messages, await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task AddAsync_DuplicateIdInOneBatch_StoresTheIdOneTime()
    {
        // Arrange
        var first = AMessage(T1, text: "first");
        var duplicate = AMessage(T1, text: "duplicate", id: first.Delivery.Id);

        // Act
        await TenantStore(T1).AddAsync([first, duplicate], Ct);

        // Assert
        Assert.Equal([first], await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task AddAsync_IdThatIsAlreadyStored_KeepsTheStoredMessage()
    {
        // Arrange
        var stored = AMessage(T1, text: "stored");
        await TenantStore(T1).AddAsync([stored], Ct);
        var again = AMessage(T1, text: "again", id: stored.Delivery.Id);

        // Act
        var error = await TenantStore(T1).AddAsync([again], Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal([stored], await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task AddAsync_TenantStoresOfTwoKeys_StoreEachMessageWithTheTenantOfItsKey()
    {
        // Arrange
        var ofT1 = AMessage(T1);
        var ofT2 = AMessage(T2);

        // Act
        await TenantStore(T1).AddAsync([ofT1], Ct);
        await TenantStore(T2).AddAsync([ofT2], Ct);

        // Assert
        Assert.Equal(
            [(ofT1.Delivery.Id, (TenantId?)new TenantId("t1")), (ofT2.Delivery.Id, new TenantId("t2"))],
            (await TestData.GetStoredAsync(Ct)).Select(static m => (m.Delivery.Id, m.Tenant)));
    }

    [Theory]
    [MemberData(nameof(OutboxStoreTestRows.OtherTenantRows), MemberType = typeof(OutboxStoreTestRows))]
    public async Task AddAsync_MessageOfAnotherTenant_ReturnsValidationFailedAndAddsNothingOfTheBatch(
        string storeTenant, string messageTenant)
    {
        // Arrange
        var key = Keys[storeTenant];
        var otherKey = Keys[messageTenant];
        OutgoingMessage[] batch = [AMessage(key), AMessage(otherKey)]; // the second message has another tenant

        // Act
        var error = await TenantStore(key).AddAsync(batch, Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, error?.Type);
        Assert.Empty(await TestData.GetStoredAsync(Ct));
    }
}
