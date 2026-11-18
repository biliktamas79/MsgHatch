using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>The rows and the drafts of <see cref="SubscriptionCommandsTestsBase{T}"/>.</summary>
public static class SubscriptionCommandsTestRows
{
    public static readonly SubscriptionOwner AcmeOwner = SubscriptionOwner.ForCompany(Scenario.Acme);

    public static SubscriptionDraft Draft(
        SubscriptionOwner owner,
        string type,
        ChannelId? channel,
        ChannelSettings? settings,
        SubscriptionKind kind = SubscriptionKind.Channel,
        SubscriptionRecipients? recipients = null,
        bool mandatory = false) =>
        new(owner, new MessageType(type), kind, channel, settings, recipients, mandatory);

    public static SubscriptionOwner User(string id) => SubscriptionOwner.ForUser(new UserId(id));

    public static SubscriptionRecipients NamedUsers(params string[] users) =>
        SubscriptionRecipients.ForUsers([.. users.Select(static user => new UserId(user))]);

    public static TheoryData<string, SubscriptionDraft> ValidationRows => new()
    {
        { "2.3.1: a company owner with no company", Draft(SubscriptionOwner.ForCompany(default), "A", EmailChannel.Id, new EmailChannelSettings()) },
        { "2.3.2: a message type that is not registered", Draft(User("1"), "Z", EmailChannel.Id, new EmailChannelSettings()) },
        { "2.3.2: a message type with no scope function", Draft(User("1"), "F", EmailChannel.Id, new EmailChannelSettings()) },
        { "M10 2.3.3: a channel that the message type has not registered", Draft(User("2"), "A", SignalRChannel.Id, new SignalRChannelSettings()) },
        { "2.3.3: an opt-out of a channel that the message type has not registered", Draft(User("1"), "A", SignalRChannel.Id, null, SubscriptionKind.OptOut) },
        { "2.3.3: a channel subscription with no settings", Draft(User("1"), "A", EmailChannel.Id, null) },
        { "2.3.3: a channel subscription with no channel", Draft(User("1"), "A", null, new EmailChannelSettings()) },
        { "M11 2.3.4: the settings of another channel", Draft(User("1"), "C", EmailChannel.Id, new SignalRChannelSettings()) },
        { "M12 2.3.6: a language that is not a known culture", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings("xx-invalid")) },
        { "M13 2.3.8: an opt-out with settings", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), SubscriptionKind.OptOut) },
        {
            "M21 2.3.10: a user subscription for the company",
            Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), recipients: SubscriptionRecipients.Company)
        },
        { "M21 2.3.11: a named user of another company", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("3")) },
        { "2.3.11: no named users", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers()) },
        { "2.3.11: a duplicate named user", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("2", "2")) },
        { "2.3.11: a named user that the directory does not know", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("99")) },
        { "M21 2.3.12: an opt-out for the company", Draft(AcmeOwner, "A", null, null, SubscriptionKind.OptOut, SubscriptionRecipients.Company) },
        { "2.3.12: a mandatory opt-out", Draft(SubscriptionOwner.Default, "A", EmailChannel.Id, null, SubscriptionKind.OptOut, mandatory: true) },
        { "M21 2.3.13: a mandatory user subscription", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), mandatory: true) },
        {
            "2.3.13: a mandatory subscription for named users",
            Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("2"), mandatory: true)
        },
    };

    public static TheoryData<string, ChannelSettings, int?> PagerRows => new()
    {
        { "2.3.4: email settings on the channel Pager, whose validator accepts them", new EmailChannelSettings(), 400 },
        { "2.3.5: a language on a channel with no feature Languages", new PagerSettings("en"), 400 },
        { "2.3.7: settings that the validator of the channel rejects", new PagerSettings(Priority: -1), 400 },
        { "valid settings", new PagerSettings(), null },
    };

    public static TheoryData<int, int?> NamedUserCountRows => new()
    {
        { 99, null },
        { 100, null },
        { 101, 400 },
    };

    public static TheoryData<string, Guid, SubscriptionDraft> UpdateValidationRows => new()
    {
        { "M17 2.3.9: #6 with the owner user 2", Scenario.S6.Id, Draft(User("2"), "B", EmailChannel.Id, new EmailChannelSettings()) },
        { "M17 2.3.9: #6 with the message type A", Scenario.S6.Id, Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings()) },
        { "2.3.9: #10 with the recipients scope users", Scenario.S10.Id, Draft(AcmeOwner, "B", EmailChannel.Id, new EmailChannelSettings()) },
    };

    public static TheoryData<string, SubscriptionDraft> ConflictRows => new()
    {
        { "M15 2.4.3: Acme, B, Email, and the opt-out #4 for all channels", Draft(AcmeOwner, "B", EmailChannel.Id, new EmailChannelSettings()) },
        { "M15 2.4.2: user 1, B, an opt-out for all channels, and #6", Draft(User("1"), "B", null, null, SubscriptionKind.OptOut) },
        { "M15 2.4.1: user 1, B, Email, and #6", Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings("hu")) },
        { "M20 2.4.5: user 1, C, an opt-out of the mandatory SignalR #3", Draft(User("1"), "C", SignalRChannel.Id, null, SubscriptionKind.OptOut) },
        { "M20 2.4.5: user 1, C, SignalR in hu, and the mandatory #3", Draft(User("1"), "C", SignalRChannel.Id, new SignalRChannelSettings("hu")) },
    };

    public static TheoryData<string, SubscriptionDraft> DirectoryCallRows => new()
    {
        { "the named users of a company subscription", Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("2")) },
        { "the company of the owner of a user subscription", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings("hu")) },
    };
}
