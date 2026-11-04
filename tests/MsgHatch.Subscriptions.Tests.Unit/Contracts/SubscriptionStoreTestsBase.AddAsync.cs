using MsgHatch.Domain.Identity;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class SubscriptionStoreTestsBase<TStore>
{
    [Fact]
    public async Task AddAsync_UserSubscription_GetAsyncGivesItUnchanged_S1()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);

        // Act
        var stored = await Store.GetAsync(Scenario.S6.Id, Ct);

        // Assert
        Assert.Equal(Scenario.S6, stored.Value);
    }

    [Fact]
    public async Task AddAsync_SameOwnerTypeChannelAndRecipientKind_GivesConflict_S2()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);

        // Act
        var error = await Store.AddAsync(Scenario.S6 with { Id = Scenario.LabelId(40), Settings = new EmailChannelSettings("hu") }, Ct);

        // Assert
        Assert.Equal(409, CodeOf(error));
    }

    [Fact]
    public async Task AddAsync_SameOwnerAndTypeOnAnotherChannel_Succeeds_S2()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);

        // Act
        var error = await Store.AddAsync(
            Scenario.S6 with { Id = Scenario.LabelId(41), Channel = SignalRChannel.Id, Settings = new SignalRChannelSettings() }, Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task AddAsync_OptOutAndSubscriptionForTheCompanyOfOneOwner_BothSucceed_S11()
    {
        // Arrange
        Assert.Null(await Store.AddAsync(Scenario.S4, Ct));

        // Act
        var error = await Store.AddAsync(Scenario.S10, Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task AddAsync_SameOwnerTypeAndChannelForTheScopeUsersAndForTheCompany_BothSucceed_S13()
    {
        // Arrange
        Assert.Null(await Store.AddAsync(Scenario.S10, Ct));

        // Act
        var error = await Store.AddAsync(Scenario.S10 with { Id = Scenario.LabelId(46), Recipients = SubscriptionRecipients.ScopeUsers }, Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task AddAsync_SecondSubscriptionForTheCompany_GivesConflict_S11()
    {
        // Arrange
        await Store.AddAsync(Scenario.S4, Ct);
        await Store.AddAsync(Scenario.S10, Ct);

        // Act
        var error = await Store.AddAsync(Scenario.S10 with { Id = Scenario.LabelId(42), Settings = new EmailChannelSettings("en") }, Ct);

        // Assert
        Assert.Equal(409, CodeOf(error));
    }

    [Fact]
    public async Task AddAsync_SubscriptionForNamedUsers_GetAsyncGivesTheNamedUserUnchanged_S11()
    {
        // Arrange
        await Store.AddAsync(Scenario.S11, Ct);

        // Act
        var stored = await Store.GetAsync(Scenario.S11.Id, Ct);

        // Assert
        Assert.Equal(Scenario.S11, stored.Value);
    }

    [Fact]
    public async Task AddAsync_MandatoryDefaultSubscription_GetAsyncGivesItUnchanged()
    {
        // Arrange
        await Store.AddAsync(Scenario.S3, Ct);

        // Act
        var stored = await Store.GetAsync(Scenario.S3.Id, Ct);

        // Assert
        Assert.Equal(Scenario.S3, stored.Value);
    }

    [Fact]
    public async Task AddAsync_OptOutForAllChannels_GetAsyncGivesItUnchanged()
    {
        // Arrange
        await Store.AddAsync(Scenario.S4, Ct);

        // Act
        var stored = await Store.GetAsync(Scenario.S4.Id, Ct);

        // Assert
        Assert.Equal(Scenario.S4, stored.Value);
    }

    [Fact]
    public async Task AddAsync_IdsWithSpecialAndAccentedCharacters_QueryAsyncFindsEachIdUnchanged_S10()
    {
        // Arrange
        var user = new UserId("auth0|abc");
        var company = new CompanyId("Ődön-Űr");
        await Store.AddAsync(Scenario.S6 with { Owner = SubscriptionOwner.ForUser(user) }, Ct);
        await Store.AddAsync(Scenario.S5 with { Owner = SubscriptionOwner.ForCompany(company) }, Ct);

        // Act
        var found = (await Store.QueryAsync(new SubscriptionQuery(User: user), Ct), await Store.QueryAsync(new SubscriptionQuery(Company: company), Ct));

        // Assert
        Assert.Equal(
            ((UserId?)user, (CompanyId?)company),
            (Assert.Single(found.Item1.Value).Owner.User, Assert.Single(found.Item2.Value).Owner.Company));
    }
}
