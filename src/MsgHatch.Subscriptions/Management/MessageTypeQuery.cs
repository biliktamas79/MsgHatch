using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Management;

/// <summary>The filters of the list of message types with their applied subscriptions (D81).</summary>
/// <param name="Type">Only this message type, or null for each message type with a scope function.</param>
/// <param name="Company">The company whose applied subscriptions the list gives.</param>
/// <param name="User">The user whose applied subscriptions the list gives. The directory gives the company of the user.</param>
public sealed record MessageTypeQuery(MessageType? Type = null, CompanyId? Company = null, UserId? User = null);
