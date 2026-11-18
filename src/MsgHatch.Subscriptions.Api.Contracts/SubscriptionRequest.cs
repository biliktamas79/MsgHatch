using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Api.Contracts;

/// <summary>
/// The body of <c>POST {prefix}</c> and <c>PUT {prefix}/{id}</c>: a subscription draft, and for the update the revision that
/// the caller read (section 4.17). The <c>/me</c> routes ignore the owner fields, and use the signed-in user (D154).
/// </summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Kind">A channel subscription or an opt-out.</param>
/// <param name="Level">The level of the owner. The routes with no <c>/me</c> need it.</param>
/// <param name="CompanyId">The company of a company owner.</param>
/// <param name="UserId">The user of a user owner.</param>
/// <param name="Channel">The channel, or null for an opt-out for all channels.</param>
/// <param name="Settings">
/// The settings of the channel, read as the newest registered settings type of the channel, for example
/// <c>{ "language": "hu", "format": "PlainText" }</c> for <c>Email</c>.
/// </param>
/// <param name="Recipients">The recipients, or null for the scope users.</param>
/// <param name="Mandatory">True for a mandatory subscription (D88).</param>
/// <param name="Revision">The revision that the caller read. <c>PUT</c> needs it, and <c>POST</c> ignores it.</param>
public sealed record SubscriptionRequest(
    string MessageType,
    SubscriptionKind Kind,
    SubscriptionLevel? Level = null,
    string? CompanyId = null,
    string? UserId = null,
    string? Channel = null,
    JsonElement? Settings = null,
    SubscriptionRecipientsBody? Recipients = null,
    bool Mandatory = false,
    int? Revision = null)
{
    /// <summary>Gives the body of a draft (D228).</summary>
    /// <param name="draft">The draft.</param>
    /// <param name="registry">The content type registry, which gives the JSON of the settings type.</param>
    /// <param name="revision">The revision that the caller read, for an update.</param>
    /// <returns>The body.</returns>
    public static SubscriptionRequest From(SubscriptionDraft draft, IContentTypeRegistry registry, int? revision = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(registry);
        return new SubscriptionRequest(
            draft.Type.Name, draft.Kind, draft.Owner.Level, draft.Owner.Company?.Value, draft.Owner.User?.Value, draft.Channel?.Name,
            draft.Settings is null ? null : SettingsJson.Write(registry, draft.Settings),
            draft.Recipients is null ? null : SubscriptionRecipientsBody.From(draft.Recipients), draft.Mandatory, revision);
    }

    /// <summary>
    /// Gives the draft of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/>, and the settings
    /// of a channel with no registered settings type give <see cref="JsonChannelSettings"/> (D228, D229).
    /// </summary>
    /// <param name="registry">The content type registry, which gives the settings type of the channel.</param>
    /// <returns>The draft, or an error.</returns>
    public ErrorOr<SubscriptionDraft> ToDraft(IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var owner = Level is { } level ? WireValues.OwnerOf(level, CompanyId, UserId) : WireValues.Invalid("A draft has the level of its owner.");
        var type = WireValues.MessageTypeOf(MessageType);
        var kind = WireValues.EnumOf(Kind, "kind");
        var channel = WireValues.OptionalChannelOf(Channel);
        var recipients = Recipients?.ToRecipients();
        var error = owner.Error ?? type.Error ?? kind.Error ?? channel.Error ?? recipients?.Error;
        if (error is not null)
        {
            return error;
        }

        var settings = SettingsOf(registry, channel.Value);
        return settings.IsError
            ? settings.Error
            : new SubscriptionDraft(owner.Value, type.Value, Kind, channel.Value, settings.Value, recipients?.Value, Mandatory);
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

        var settings = SettingsJson.ReadFromTheWire(registry, id, json);
        return settings.IsError ? settings.Error : settings.Value;
    }
}

/// <summary>The recipients of a subscription in a request or a response body (D89).</summary>
/// <param name="Kind">The kind of the recipients.</param>
/// <param name="Users">The named users, only for <see cref="RecipientKind.NamedUsers"/>.</param>
public sealed record SubscriptionRecipientsBody(RecipientKind Kind, IReadOnlyList<string>? Users = null)
{
    /// <summary>Gives the body of the recipients (D228).</summary>
    /// <param name="recipients">The recipients.</param>
    /// <returns>The body.</returns>
    public static SubscriptionRecipientsBody From(SubscriptionRecipients recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        return new SubscriptionRecipientsBody(recipients.Kind, [.. recipients.Users.Select(static user => user.Value)]);
    }

    /// <summary>Gives the recipients of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The recipients, or an error.</returns>
    public ErrorOr<SubscriptionRecipients> ToRecipients()
    {
        var users = Users ?? [];
        return Kind switch
        {
            RecipientKind.ScopeUsers when users.Count == 0 => SubscriptionRecipients.ScopeUsers,
            RecipientKind.Company when users.Count == 0 => SubscriptionRecipients.Company,
            RecipientKind.NamedUsers => NamedUsersOf(users),
            _ => WireValues.Invalid("The recipients are ScopeUsers or Company with no users, or NamedUsers."),
        };
    }

    private static ErrorOr<SubscriptionRecipients> NamedUsersOf(IReadOnlyList<string> users)
    {
        var ids = new Domain.Identity.UserId[users.Count];
        for (var index = 0; index < users.Count; index++)
        {
            var id = WireValues.UserOf(users[index]);
            if (id.IsError)
            {
                return id.Error;
            }

            ids[index] = id.Value;
        }

        return SubscriptionRecipients.ForUsers(ids);
    }
}
