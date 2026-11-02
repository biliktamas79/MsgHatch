using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Email;

/// <summary>The email channel (section 4.4).</summary>
public static class EmailChannel
{
    /// <summary>The id of the email channel.</summary>
    public static readonly ChannelId Id = new("Email");

    /// <summary>The descriptor of the email channel, with languages and rich text (D77).</summary>
    public static readonly ChannelDescriptor<EmailChannelSettings> Descriptor =
        new(Id, "Email", ChannelFeatures.Languages | ChannelFeatures.RichText);
}
