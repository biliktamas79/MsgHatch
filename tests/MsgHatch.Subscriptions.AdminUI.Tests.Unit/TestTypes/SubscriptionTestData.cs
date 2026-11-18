using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes;

public sealed record FaxSettings(string Number) : ChannelSettings;

public sealed record UnknownChannelDescriptor(ChannelId Id, string Name) : ChannelDescriptor(Id, Name, ChannelFeatures.None)
{
    public override Type SettingsType => typeof(ChannelSettings);
}

public static class SubscriptionTestData
{
    public static readonly MessageType Requests = new("Requests");
    public static readonly MessageType Notices = new("Notices");
    public static readonly ChannelId Fax = new("Fax");
    public static readonly ChannelId Pager = new("Pager");
    public static readonly Guid Id = Guid.Parse("3f2c6b1e-7a40-4c1d-9a7e-1b2c3d4e5f60");

    public static readonly ContentTypeEntry FaxSettingsEntry =
        new("Settings:Fax", 1, "Settings:Fax@1", typeof(FaxSettings), JsonSerializerOptions.Web.GetTypeInfo(typeof(FaxSettings)));

    public static IReadOnlyList<ChannelDescriptor> Channels { get; } =
        [new ChannelDescriptor<FaxSettings>(Fax, "Fax", ChannelFeatures.None), new UnknownChannelDescriptor(Pager, "Pager")];

    public static Subscription Subscription(
        SubscriptionOwner? owner = null, MessageType? type = null, SubscriptionKind kind = SubscriptionKind.Channel, ChannelId? channel = null,
        ChannelSettings? settings = null, int revision = 3, Guid? id = null, SubscriptionRecipients? recipients = null) =>
        new(id ?? Id, revision, owner ?? SubscriptionOwner.ForCompany(new CompanyId("42")), type ?? Requests, kind, channel ?? Fax,
            settings ?? new FaxSettings("123"), recipients ?? SubscriptionRecipients.ScopeUsers, Mandatory: false,
            new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    public static ValueTask<ErrorOr<T>> Ok<T>(T value) => ValueTask.FromResult(ErrorOr<T>.ForValue(value));

    public static ValueTask<ErrorOr<T>> Fail<T>(ErrorType type, string? message = null) => ValueTask.FromResult(ErrorOr<T>.ForError(new Error(type, message)));
}
