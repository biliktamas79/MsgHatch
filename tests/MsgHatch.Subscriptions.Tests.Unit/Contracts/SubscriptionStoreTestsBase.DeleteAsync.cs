using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class SubscriptionStoreTestsBase<TStore>
{
    [Fact]
    public async Task DeleteAsync_StoredSubscription_GetAsyncGivesNull_S6()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        Assert.Null(await Store.DeleteAsync(Scenario.S6.Id, Ct));

        // Act
        var stored = await Store.GetAsync(Scenario.S6.Id, Ct);

        // Assert
        Assert.Equal((null, null), (stored.Error, stored.Value));
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_Succeeds_S6()
    {
        // Arrange
        var unknown = Scenario.LabelId(43);

        // Act
        var error = await Store.DeleteAsync(unknown, Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task GetAsync_UnknownId_GivesNull()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);

        // Act
        var stored = await Store.GetAsync(Scenario.LabelId(44), Ct);

        // Assert
        Assert.Equal((null, null), (stored.Error, stored.Value));
    }
}
