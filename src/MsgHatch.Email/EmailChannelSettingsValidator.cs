using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Email;

/// <summary>
/// Checks the settings of an email route (D77): the settings type, a known format, and the feature <c>RichText</c>
/// for the format <see cref="EmailFormat.Html"/>. The subscription manager checks the language (section 4.17).
/// </summary>
public sealed class EmailChannelSettingsValidator : IChannelSettingsValidator
{
    /// <inheritdoc/>
    public ChannelId Channel => EmailChannel.Id;

    /// <inheritdoc/>
    public Error? Validate(ChannelSettings settings, ChannelFeatures features) => settings switch
    {
        not EmailChannelSettings => new Error(Errors.ValidationFailed, "The settings are not email settings."),
        EmailChannelSettings { Format: var format } when !Enum.IsDefined(format) =>
            new Error(Errors.ValidationFailed, $"The email format {format} is not known."),
        EmailChannelSettings { Format: EmailFormat.Html } when !features.HasFlag(ChannelFeatures.RichText) =>
            new Error(Errors.ValidationFailed, "The email format Html needs the feature RichText."),
        _ => null,
    };
}
