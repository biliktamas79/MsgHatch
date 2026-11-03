using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.SignalR;

/// <summary>
/// Checks the settings of a SignalR route (D77): only the settings type, because each SignalR settings value is valid.
/// The subscription manager checks the language (section 4.17).
/// </summary>
public sealed class SignalRChannelSettingsValidator : IChannelSettingsValidator
{
    /// <inheritdoc/>
    public ChannelId Channel => SignalRChannel.Id;

    /// <inheritdoc/>
    public Error? Validate(ChannelSettings settings, ChannelFeatures features) =>
        settings is SignalRChannelSettings ? null : new Error(Errors.ValidationFailed, "The settings are not SignalR settings.");
}
