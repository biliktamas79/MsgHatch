using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>Checks the settings of one channel (D77).</summary>
public interface IChannelSettingsValidator
{
    /// <summary>The channel of the validator.</summary>
    ChannelId Channel { get; }

    /// <summary>Checks the settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="features">The features of the channel.</param>
    /// <returns>Null for valid settings, else the error.</returns>
    Error? Validate(ChannelSettings settings, ChannelFeatures features);
}
