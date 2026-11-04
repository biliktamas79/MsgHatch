using MsgHatch.Email;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class SubscriptionStoreTestsBase<TStore>
{
    [Fact]
    public async Task UpdateAsync_ExpectedRevision1_GetAsyncGivesRevision2_S3()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        Assert.Null(await Store.UpdateAsync(Scenario.S6 with { Revision = 2 }, 1, Ct));

        // Act
        var stored = await Store.GetAsync(Scenario.S6.Id, Ct);

        // Assert
        Assert.Equal(2, stored.Value!.Revision);
    }

    [Fact]
    public async Task UpdateAsync_NewSettingsAndChangeTime_GetAsyncGivesTheWholeNewState()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        var changed = Scenario.S6 with
        {
            Revision = 2, Settings = new EmailChannelSettings("hu", EmailFormat.Html), LastModifiedAtUtc = Scenario.S6.CreatedAtUtc.AddHours(1),
        };
        Assert.Null(await Store.UpdateAsync(changed, 1, Ct));

        // Act
        var stored = await Store.GetAsync(Scenario.S6.Id, Ct);

        // Assert
        Assert.Equal(changed, stored.Value);
    }

    [Fact]
    public async Task UpdateAsync_OldExpectedRevision_GivesConflict_S4()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        await Store.UpdateAsync(Scenario.S6 with { Revision = 2 }, 1, Ct);

        // Act
        var error = await Store.UpdateAsync(Scenario.S6 with { Revision = 2, Settings = new EmailChannelSettings("hu") }, 1, Ct);

        // Assert
        Assert.Equal(409, CodeOf(error));
    }

    [Fact]
    public async Task UpdateAsync_OldExpectedRevision_KeepsTheStoredSubscription_S4()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        await Store.UpdateAsync(Scenario.S6 with { Revision = 2 }, 1, Ct);
        await Store.UpdateAsync(Scenario.S6 with { Revision = 2, Settings = new EmailChannelSettings("hu") }, 1, Ct);

        // Act
        var stored = await Store.GetAsync(Scenario.S6.Id, Ct);

        // Assert
        Assert.Equal(Scenario.S6 with { Revision = 2 }, stored.Value);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_GivesNotFound_S5()
    {
        // Arrange
        var unknown = Scenario.S6;

        // Act
        var error = await Store.UpdateAsync(unknown, 1, Ct);

        // Assert
        Assert.Equal(404, CodeOf(error));
    }

    [Fact]
    public async Task UpdateAsync_ToTheChannelOfAnotherSubscriptionOfTheOwner_GivesConflict()
    {
        // Arrange
        await Store.AddAsync(Scenario.S8, Ct);
        await Store.AddAsync(Scenario.S9, Ct);

        // Act
        var error = await Store.UpdateAsync(Scenario.S9 with { Revision = 2, Channel = Scenario.S8.Channel, Settings = Scenario.S8.Settings }, 1, Ct);

        // Assert
        Assert.Equal(409, CodeOf(error));
    }
}
