using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Tests.Unit;

public sealed class SubscriptionRecipientsUnitTests
{
    [Fact]
    public void Equals_SameUsersInNewLists_AreEqualWithTheSameHashCode_V5()
    {
        // Arrange
        var first = SubscriptionRecipients.ForUsers([new UserId("1"), new UserId("2")]);

        // Act
        var second = SubscriptionRecipients.ForUsers(new List<UserId> { new("1"), new("2") });

        // Assert
        Assert.Equal((first, first.GetHashCode()), (second, second.GetHashCode()));
    }

    [Fact]
    public void Equals_SameUsersInAnotherOrder_AreNotEqual_V5()
    {
        // Arrange
        var first = SubscriptionRecipients.ForUsers([new UserId("1"), new UserId("2")]);

        // Act
        var third = SubscriptionRecipients.ForUsers([new UserId("2"), new UserId("1")]);

        // Assert
        Assert.NotEqual(first, third);
    }

    [Fact]
    public void ForUsers_ListChangedLater_KeepsTheUsersOfTheCall()
    {
        // Arrange
        var users = new List<UserId> { new("1") };
        var recipients = SubscriptionRecipients.ForUsers(users);

        // Act
        users.Add(new UserId("2"));

        // Assert
        Assert.Equal([new UserId("1")], recipients.Users);
    }

    [Fact]
    public void ScopeUsersAndCompany_StaticValues_HaveTheirKindsAndNoUsers()
    {
        // Arrange
        SubscriptionRecipients[] recipients = [SubscriptionRecipients.ScopeUsers, SubscriptionRecipients.Company];

        // Act
        var values = recipients.Select(static value => (value.Kind, value.Users.Count));

        // Assert
        Assert.Equal([(RecipientKind.ScopeUsers, 0), (RecipientKind.Company, 0)], values);
    }

    [Fact]
    public void Equals_DifferentKindsWithNoUsers_AreNotEqual()
    {
        // Arrange
        var scopeUsers = SubscriptionRecipients.ScopeUsers;

        // Act
        var company = SubscriptionRecipients.Company;

        // Assert
        Assert.NotEqual(scopeUsers, company);
    }
}
