using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>The profile of a user from the recipient directory (D76).</summary>
/// <param name="Id">The user.</param>
/// <param name="Company">The company of the user, or null.</param>
/// <param name="UiLanguage">The UI language of the user, or null.</param>
/// <param name="Addresses">One address for each channel that needs one, for example the email address under the email channel.</param>
public sealed record UserProfile(
    UserId Id, CompanyId? Company, string? UiLanguage, IReadOnlyDictionary<ChannelId, string> Addresses);
