using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>The filters of a list of stored subscriptions. A null filter matches each value.</summary>
/// <param name="Type">The message type.</param>
/// <param name="Company">The company of a company owner.</param>
/// <param name="User">The user of a user owner.</param>
/// <param name="Level">The level of the owner.</param>
public sealed record SubscriptionQuery(
    MessageType? Type = null, CompanyId? Company = null, UserId? User = null, SubscriptionLevel? Level = null);
