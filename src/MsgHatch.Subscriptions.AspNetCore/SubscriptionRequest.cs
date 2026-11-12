using System.Text.Json;

namespace MsgHatch.Subscriptions.AspNetCore;

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
    int? Revision = null);

/// <summary>The recipients of a subscription in a request or a response body (D89).</summary>
/// <param name="Kind">The kind of the recipients.</param>
/// <param name="Users">The named users, only for <see cref="RecipientKind.NamedUsers"/>.</param>
public sealed record SubscriptionRecipientsBody(RecipientKind Kind, IReadOnlyList<string>? Users = null);
