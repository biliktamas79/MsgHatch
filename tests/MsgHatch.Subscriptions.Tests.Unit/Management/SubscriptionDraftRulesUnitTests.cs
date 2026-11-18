using MsgHatch.Domain.Identity;
using MsgHatch.Email;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using static MsgHatch.Subscriptions.Tests.Unit.Contracts.SubscriptionCommandsTestRows;

namespace MsgHatch.Subscriptions.Tests.Unit.Management;

/// <summary>The rules of a draft that need no state (D234), with a row for each rule of 2.3 that needs no state.</summary>
public sealed class SubscriptionDraftRulesUnitTests
{
    public static TheoryData<string, SubscriptionDraft, int?> RuleRows => new()
    {
        { "a valid channel subscription", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings()), null },
        { "a valid opt-out for all channels", Draft(User("1"), "A", null, null, SubscriptionKind.OptOut), null },
        { "a valid company subscription for named users", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("2", "3")), null },
        { "2.3.1: a company owner with no company", Draft(SubscriptionOwner.ForCompany(default), "A", EmailChannel.Id, new EmailChannelSettings()), 400 },
        { "2.3.1: a user owner with no user", Draft(SubscriptionOwner.ForUser(default), "A", EmailChannel.Id, new EmailChannelSettings()), 400 },
        { "2.3.8: an opt-out with settings", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), SubscriptionKind.OptOut), 400 },
        { "2.3.12: an opt-out for the company", Draft(AcmeOwner, "A", null, null, SubscriptionKind.OptOut, SubscriptionRecipients.Company), 400 },
        { "2.3.12: a mandatory opt-out", Draft(SubscriptionOwner.Default, "A", EmailChannel.Id, null, SubscriptionKind.OptOut, mandatory: true), 400 },
        { "2.3.3: a channel subscription with no channel", Draft(User("1"), "A", null, new EmailChannelSettings()), 400 },
        { "2.3.3: a channel subscription with no settings", Draft(User("1"), "A", EmailChannel.Id, null), 400 },
        { "2.3.10: a user subscription for the company", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), recipients: SubscriptionRecipients.Company), 400 },
        { "2.3.13: a mandatory user subscription", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), mandatory: true), 400 },
        { "2.3.13: a mandatory subscription for named users", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("2"), mandatory: true), 400 },
        { "2.3.11: no named users", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers()), 400 },
        { "2.3.11: a duplicate named user", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("2", "2")), 400 },
        { "2.3.11: 101 named users", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsersFrom1To(101)), 400 },
        { "2.3.11: 100 named users", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsersFrom1To(100)), null },
    };

    [Theory]
    [MemberData(nameof(RuleRows))]
    public void Check_Draft_GivesTheCodeOfTheRule(string row, SubscriptionDraft draft, int? expected)
    {
        // Act
        var error = SubscriptionDraftRules.Check(draft);

        // Assert
        Assert.Equal((row, expected), (row, error?.Type.Code));
    }

    [Fact]
    public void Check_DraftWithAnUnregisteredTypeAndAnUnknownChannel_GivesNoErrorBecauseTheRulesNeedNoState()
    {
        // Arrange
        var draft = Draft(User("99"), "Unregistered", new ChannelId("Unknown"), new EmailChannelSettings("xx-invalid"));

        // Act
        var error = SubscriptionDraftRules.Check(draft);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public void Check_NullDraft_ThrowsArgumentNullException()
    {
        // Act
        var exception = Record.Exception(static () => SubscriptionDraftRules.Check(null!));

        // Assert
        Assert.Equal("draft", Assert.IsType<ArgumentNullException>(exception).ParamName);
    }

    private static SubscriptionRecipients NamedUsersFrom1To(int count) =>
        SubscriptionRecipients.ForUsers([.. Enumerable.Range(1, count).Select(static n => new UserId($"n{n}"))]);
}
