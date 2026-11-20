using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Api.Contracts;
using static MsgHatch.Subscriptions.AdminUI.SubscriptionTexts;

namespace MsgHatch.Subscriptions.AdminUI;

internal sealed record SubscriptionForm
{
    public SubscriptionLevel Level { get; set; }

    public string CompanyId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string MessageType { get; set; } = string.Empty;

    public SubscriptionKind Kind { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string Settings { get; set; } = string.Empty;

    public RecipientKind Recipients { get; set; }

    public string Users { get; set; } = string.Empty;

    public bool Mandatory { get; set; }

    public static SubscriptionForm From(Subscription subscription, IContentTypeRegistry registry) => new()
    {
        Level = subscription.Owner.Level,
        CompanyId = subscription.Owner.Company?.Value ?? string.Empty,
        UserId = subscription.Owner.User?.Value ?? string.Empty,
        MessageType = subscription.Type.Name,
        Kind = subscription.Kind,
        Channel = subscription.Channel?.Name ?? string.Empty,
        Settings = subscription.Settings is null ? string.Empty : IndentedTextOf(SettingsJson.Write(registry, subscription.Settings)),
        Recipients = subscription.Recipients.Kind,
        Users = string.Join('\n', subscription.Recipients.Users.Select(static user => user.Value)),
        Mandatory = subscription.Mandatory,
    };

    public IReadOnlyList<string> ChangesFrom(SubscriptionForm stored)
    {
        var changes = new List<string>();
        AddWhen(changes, "owner", Level != stored.Level || CompanyId != stored.CompanyId || UserId != stored.UserId);
        AddWhen(changes, "message type", MessageType != stored.MessageType);
        AddWhen(changes, "kind", Kind != stored.Kind);
        AddWhen(changes, "channel", Channel != stored.Channel);
        AddWhen(changes, "settings", Settings != stored.Settings);
        AddWhen(changes, "recipients", Recipients != stored.Recipients || Users != stored.Users);
        AddWhen(changes, "mandatory", Mandatory != stored.Mandatory);
        return changes;
    }

    public ErrorOr<SubscriptionDraft> ToDraft(IReadOnlyList<ChannelDescriptor> channels, IContentTypeRegistry registry)
    {
        var owner = OwnerOf();
        var type = MessageTypeOf(MessageType);
        var channel = OptionalOf(Channel) is { } name ? ChannelOf(name).ThenIfSuccess<ChannelId?>(static id => id) : (ChannelId?)null;
        var recipients = RecipientsOf();
        var firstError = owner.Error ?? type.Error ?? channel.Error ?? recipients.Error;
        if (firstError is not null)
        {
            return firstError;
        }

        var settings = Kind == SubscriptionKind.OptOut ? (ChannelSettings?)null : SettingsOf(channel.Value, channels, registry);
        return settings.IsError ? settings.Error
            : new SubscriptionDraft(owner.Value, type.Value, Kind, channel.Value, settings.Value, recipients.Value, Mandatory);
    }

    private ErrorOr<SubscriptionOwner> OwnerOf() => Level switch
    {
        SubscriptionLevel.Company => Domain.Identity.CompanyId.Create(CompanyId.Trim()).ThenIfSuccess<SubscriptionOwner>(static id => SubscriptionOwner.ForCompany(id)),
        SubscriptionLevel.User => Domain.Identity.UserId.Create(UserId.Trim()).ThenIfSuccess<SubscriptionOwner>(static id => SubscriptionOwner.ForUser(id)),
        _ => SubscriptionOwner.Default,
    };

    private ErrorOr<SubscriptionRecipients> RecipientsOf() => Recipients switch
    {
        RecipientKind.Company => SubscriptionRecipients.Company,
        RecipientKind.NamedUsers => UsersOf(Users).ThenIfSuccess(static users => ErrorOr<SubscriptionRecipients>.ForValue(SubscriptionRecipients.ForUsers(users))),
        _ => SubscriptionRecipients.ScopeUsers,
    };

    private ErrorOr<ChannelSettings?> SettingsOf(ChannelId? channel, IReadOnlyList<ChannelDescriptor> channels, IContentTypeRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(Settings))
        {
            return (ChannelSettings?)null;
        }

        JsonElement json;
        try
        {
            using var document = JsonDocument.Parse(Settings);
            json = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            return Invalid($"The settings are not valid JSON: {exception.Message}");
        }

        return channel is { } id && HasSettingsType(id, channels)
            ? SettingsJson.Read(registry, id, json).ThenIfSuccess(static settings => ErrorOr<ChannelSettings?>.ForValue(settings))
            : new JsonChannelSettings(json);
    }

    private static bool HasSettingsType(ChannelId channel, IReadOnlyList<ChannelDescriptor> channels) =>
        channels.FirstOrDefault(descriptor => descriptor.Id == channel) is { } descriptor && descriptor.SettingsType != typeof(ChannelSettings);

    private static string IndentedTextOf(JsonElement json)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void AddWhen(List<string> changes, string field, bool changed)
    {
        if (changed)
        {
            changes.Add(field);
        }
    }
}
