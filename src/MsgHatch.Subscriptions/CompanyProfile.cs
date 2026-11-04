using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>The profile of a company from the recipient directory (D76).</summary>
/// <param name="Id">The company.</param>
/// <param name="DefaultLanguage">The default language of the company, or null.</param>
/// <param name="Addresses">The addresses of the company itself, for example the email address of its administrator.</param>
public sealed record CompanyProfile(CompanyId Id, string? DefaultLanguage, IReadOnlyDictionary<ChannelId, string> Addresses);
