using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class SubscriptionStoreTestsBase<TStore>
{
    [Fact]
    public async Task FindForDeliveryAsync_BForAcmeAndUsers1And2_GivesTheDefaultsAndTheSubscriptionsOfTheOwners_S8()
    {
        // Arrange
        await SeedScenarioAsync();

        // Act
        var subscriptions = await Store.FindForDeliveryAsync(new MessageType("B"), [Scenario.Acme], [new UserId("1"), new UserId("2")], Ct);

        // Assert
        Assert.Equal("#10, #2, #4, #6", Labels(subscriptions));
    }

    [Fact]
    public async Task FindForDeliveryAsync_ANoCompaniesAndNoUsers_GivesTheDefaults_S9()
    {
        // Arrange
        await SeedScenarioAsync();

        // Act
        var subscriptions = await Store.FindForDeliveryAsync(new MessageType("A"), [], [], Ct);

        // Assert
        Assert.Equal("#1", Labels(subscriptions));
    }

    [Fact]
    public async Task FindForDeliveryAsync_DForGlobexAndUser1_GivesNoSubscriptionOfAcmeOrOfUser2()
    {
        // Arrange
        await SeedScenarioAsync();

        // Act
        var subscriptions = await Store.FindForDeliveryAsync(new MessageType("D"), [Scenario.Globex], [new UserId("1")], Ct);

        // Assert
        Assert.Equal("#8, #9", Labels(subscriptions));
    }

    [Fact]
    public async Task AddAsync_SameSubscriptionInTheStoresOfTwoKeys_BothSucceed_S12()
    {
        // Arrange
        Assert.Null(await StoreOf(TenantKey.None).AddAsync(Scenario.S6, Ct));

        // Act
        var error = await StoreOf(T2).AddAsync(Scenario.S6 with { Id = Scenario.LabelId(45) }, Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task FindForDeliveryAsync_StoresOfTwoKeys_EachGivesOnlyItsOwnSubscription_S12()
    {
        // Arrange
        await StoreOf(TenantKey.None).AddAsync(Scenario.S6, Ct);
        await StoreOf(T2).AddAsync(Scenario.S6 with { Id = Scenario.LabelId(45) }, Ct);

        // Act
        string[] found =
        [
            Labels(await StoreOf(TenantKey.None).FindForDeliveryAsync(new MessageType("B"), [], [new UserId("1")], Ct)),
            Labels(await StoreOf(T2).FindForDeliveryAsync(new MessageType("B"), [], [new UserId("1")], Ct)),
        ];

        // Assert
        Assert.Equal(["#6", "#45"], found);
    }
}
