using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>Factories of valid subscriptions and profiles, with defaults that a test changes with named parameters.</summary>
public static class TestSubscriptions
{
    public static readonly DateTime CreatedAtUtc = TestHost.Start.UtcDateTime;
    public static readonly ChannelId TestChannel = new("Test");

    public static Subscription A(
        Guid? id = null,
        SubscriptionOwner? owner = null,
        string type = "A",
        SubscriptionKind kind = SubscriptionKind.Channel,
        ChannelId? channel = null,
        ChannelSettings? settings = null,
        SubscriptionRecipients? recipients = null,
        bool mandatory = false,
        int revision = 1) =>
        new(
            id ?? Guid.NewGuid(), revision, owner ?? SubscriptionOwner.Default, new MessageType(type), kind,
            kind == SubscriptionKind.OptOut ? channel : channel ?? TestChannel,
            kind == SubscriptionKind.OptOut ? settings : settings ?? new TestChannelSettings(),
            recipients ?? SubscriptionRecipients.ScopeUsers, mandatory, CreatedAtUtc, CreatedAtUtc);

    public static UserProfile AUser(string id = "1", string? company = "100", string? uiLanguage = "en", string? email = null) =>
        new(
            new UserId(id), company is null ? null : new CompanyId(company), uiLanguage,
            email is null ? new Dictionary<ChannelId, string>() : new Dictionary<ChannelId, string> { [new ChannelId("Email")] = email });

    public static CompanyProfile ACompany(string id = "100", string? defaultLanguage = "hu", string? email = null) =>
        new(
            new CompanyId(id), defaultLanguage,
            email is null ? new Dictionary<ChannelId, string>() : new Dictionary<ChannelId, string> { [new ChannelId("Email")] = email });
}

/// <summary>The settings of the test channel of <see cref="TestSubscriptions.TestChannel"/>.</summary>
public sealed record TestChannelSettings : ChannelSettings;
