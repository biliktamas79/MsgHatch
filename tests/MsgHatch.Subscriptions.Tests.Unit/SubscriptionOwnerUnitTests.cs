using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Tests.Unit;

public sealed class SubscriptionOwnerUnitTests
{
    [Fact]
    public void Owners_DefaultCompanyAndUser_HaveTheirLevelsAndOnlyTheirOwnIds_V1()
    {
        // Arrange
        var company = new CompanyId("100");
        var user = new UserId("1");

        // Act
        SubscriptionOwner[] owners = [SubscriptionOwner.Default, SubscriptionOwner.ForCompany(company), SubscriptionOwner.ForUser(user)];

        // Assert
        Assert.Equal(
            [(SubscriptionLevel.Default, null, null), (SubscriptionLevel.Company, company, null), (SubscriptionLevel.User, null, (UserId?)user)],
            owners.Select(static owner => (owner.Level, owner.Company, owner.User)));
    }
}
