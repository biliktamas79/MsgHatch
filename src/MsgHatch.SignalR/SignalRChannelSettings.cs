using MsgHatch.Domain.Routing;

namespace MsgHatch.SignalR;

/// <summary>The settings of a SignalR route.</summary>
/// <param name="Language">
/// The language, or null for the UI language of the user, or the default language of the company for a company target.
/// </param>
public sealed record SignalRChannelSettings(string? Language = null) : ChannelSettings;
