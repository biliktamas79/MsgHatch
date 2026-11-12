using System.Text.Json;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Subscriptions.AspNetCore;

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
    internal static SubscriptionResponse From(Subscription subscription, IContentTypeRegistry registry) =>
        new(
            subscription.Id, subscription.Revision, subscription.Owner.Level, subscription.Owner.Company?.Value,
            subscription.Owner.User?.Value, subscription.Type.Name, subscription.Kind, subscription.Channel?.Name,
            subscription.Settings is null ? null : SettingsJson.Write(registry, subscription.Settings),
            new SubscriptionRecipientsBody(subscription.Recipients.Kind, [.. subscription.Recipients.Users.Select(static user => user.Value)]),
            subscription.Mandatory, subscription.CreatedAtUtc, subscription.LastModifiedAtUtc);
}
