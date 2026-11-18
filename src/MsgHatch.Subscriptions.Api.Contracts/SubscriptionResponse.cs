using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Api.Contracts;

/// <summary>One stored subscription in a response body, with the field names of <see cref="SubscriptionRequest"/>.</summary>
/// <param name="Id">The id.</param>
/// <param name="Revision">The revision. A <c>PUT</c> gives it back.</param>
/// <param name="Level">The level of the owner.</param>
/// <param name="CompanyId">The company of a company owner, else null.</param>
/// <param name="UserId">The user of a user owner, else null.</param>
/// <param name="MessageType">The message type.</param>
/// <param name="Kind">A channel subscription or an opt-out.</param>
/// <param name="Channel">The channel, or null for an opt-out for all channels.</param>
/// <param name="Settings">The settings of the channel as the JSON of their settings type, or null for an opt-out.</param>
/// <param name="Recipients">The recipients.</param>
/// <param name="Mandatory">True for a mandatory subscription (D88).</param>
/// <param name="CreatedAtUtc">The time of the create.</param>
/// <param name="LastModifiedAtUtc">The time of the last change.</param>
public sealed record SubscriptionResponse(
    Guid Id,
    int Revision,
    SubscriptionLevel Level,
    string? CompanyId,
    string? UserId,
    string MessageType,
    SubscriptionKind Kind,
    string? Channel,
    JsonElement? Settings,
    SubscriptionRecipientsBody Recipients,
    bool Mandatory,
    DateTime CreatedAtUtc,
    DateTime LastModifiedAtUtc)
{
    /// <summary>Gives the body of a stored subscription (D228).</summary>
    /// <param name="subscription">The subscription.</param>
    /// <param name="registry">The content type registry, which gives the JSON of the settings type.</param>
    /// <returns>The body.</returns>
    public static SubscriptionResponse From(Subscription subscription, IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(registry);
        return new SubscriptionResponse(
            subscription.Id, subscription.Revision, subscription.Owner.Level, subscription.Owner.Company?.Value,
            subscription.Owner.User?.Value, subscription.Type.Name, subscription.Kind, subscription.Channel?.Name,
            subscription.Settings is null ? null : SettingsJson.Write(registry, subscription.Settings),
            SubscriptionRecipientsBody.From(subscription.Recipients), subscription.Mandatory, subscription.CreatedAtUtc, subscription.LastModifiedAtUtc);
    }

    /// <summary>
    /// Gives the subscription of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/>, and the
    /// settings of a channel with no registered settings type give <see cref="JsonChannelSettings"/> (D228, D229).
    /// </summary>
    /// <param name="registry">The content type registry, which gives the settings type of the channel.</param>
    /// <returns>The subscription, or an error.</returns>
    public ErrorOr<Subscription> ToSubscription(IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var owner = WireValues.OwnerOf(Level, CompanyId, UserId);
        var type = WireValues.MessageTypeOf(MessageType);
        var kind = WireValues.EnumOf(Kind, "kind");
        var channel = WireValues.OptionalChannelOf(Channel);
        var recipients = Recipients is null ? WireValues.Invalid("A subscription has recipients.") : Recipients.ToRecipients();
        var error = owner.Error ?? type.Error ?? kind.Error ?? channel.Error ?? recipients.Error;
        if (error is not null)
        {
            return error;
        }

        var settings = SettingsOf(registry, channel.Value);
        return settings.IsError
            ? settings.Error
            : new Subscription(
                Id, Revision, owner.Value, type.Value, Kind, channel.Value, settings.Value, recipients.Value, Mandatory, CreatedAtUtc, LastModifiedAtUtc);
    }

    private ErrorOr<ChannelSettings?> SettingsOf(IContentTypeRegistry registry, Domain.Identity.ChannelId? channel)
    {
        if (Settings is not { } json)
        {
            return (ChannelSettings?)null;
        }

        if (channel is not { } id)
        {
            return WireValues.Invalid("Settings need a channel.");
        }

        var settings = SettingsJson.ReadFromTheWire(registry, settingsType: null, id, json);
        return settings.IsError ? settings.Error : settings.Value;
    }
}
