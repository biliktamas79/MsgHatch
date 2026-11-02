using MsgHatch.Domain.Routing;

namespace MsgHatch.Email;

/// <summary>The settings of an email route.</summary>
/// <param name="Language">
/// The language, or null for the UI language of the user, or the default language of the company for a company target.
/// </param>
/// <param name="Format">The format of the email.</param>
public sealed record EmailChannelSettings(string? Language = null, EmailFormat Format = EmailFormat.Html) : ChannelSettings;
