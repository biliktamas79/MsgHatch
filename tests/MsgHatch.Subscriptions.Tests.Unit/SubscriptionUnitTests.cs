using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit;

public sealed class SubscriptionUnitTests
{
    public static TheoryData<string, bool, RecipientKind, bool> IsFixedRows => new()
    {
        { "#1: scope users, not mandatory", false, RecipientKind.ScopeUsers, false },
        { "#3: scope users, mandatory", true, RecipientKind.ScopeUsers, true },
        { "#10: the company", false, RecipientKind.Company, true },
        { "#11: named users", false, RecipientKind.NamedUsers, true },
    };

    [Theory]
    [MemberData(nameof(IsFixedRows))]
    public void IsFixed_MandatoryFlagAndRecipients_GivesTheRuleOfD89(string row, bool mandatory, RecipientKind kind, bool expected)
    {
        // Arrange
        var recipients = new SubscriptionRecipients(kind, kind == RecipientKind.NamedUsers ? [new UserId("2")] : []);
        var subscription = TestSubscriptions.A(recipients: recipients, mandatory: mandatory);

        // Act
        var isFixed = subscription.IsFixed;

        // Assert
        Assert.Equal((row, expected), (row, isFixed));
    }

    [Fact]
    public void Equals_NamedUsersReadBackInNewLists_AreEqual()
    {
        // Arrange
        var id = Guid.NewGuid();
        var stored = TestSubscriptions.A(id: id, recipients: SubscriptionRecipients.ForUsers([new UserId("2")]));

        // Act
        var readBack = stored with { Recipients = SubscriptionRecipients.ForUsers(new List<UserId> { new("2") }) };

        // Assert
        Assert.Equal(stored, readBack);
    }
}
