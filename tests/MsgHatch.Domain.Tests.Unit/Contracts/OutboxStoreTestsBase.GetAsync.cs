using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
{
    [Fact]
    public async Task GetAsync_MessageWithTwoTries_GivesTheStoredMessageAndAllTriesInOrder()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1, text: "Ősz-űr \"quoted\" <tag> & more"));
        PublishTry[] tries = [ATry(message, 1, PublishOutcome.TransientError, errorMessage: "Too many"), ATry(message, 2, PublishOutcome.Succeeded)];
        await Store.RecordTryAsync(message, tries[0], Now.AddMinutes(1), Ct);
        await Store.RecordTryAsync(message, tries[1], nextTryAtUtc: null, Ct);

        // Act
        var history = await TenantStore(T1).GetAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal(await StoredAsync(message.Delivery.Id), history.Value!.Message);
        Assert.Equal(tries, history.Value.Tries);
    }

    [Fact]
    public async Task GetAsync_UnknownId_GivesNull()
    {
        // Arrange
        await AddAsync(AMessage(T1));

        // Act
        var history = await TenantStore(T1).GetAsync(Guid.NewGuid(), Ct);

        // Assert
        Assert.Equal((false, null), (history.IsError, history.Value));
    }

    [Fact]
    public async Task GetAsync_IdOfAnotherTenant_GivesNull()
    {
        // Arrange
        var message = await AddAsync(AMessage(T2));

        // Act
        var history = await TenantStore(T1).GetAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal((false, null), (history.IsError, history.Value));
    }
}
