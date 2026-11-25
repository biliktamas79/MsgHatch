using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Templates;

namespace MsgHatch.Channels.Email;

/// <summary>The email channel (section 4.4).</summary>
public static class EmailChannel
{
    /// <summary>The id of the email channel.</summary>
    public static readonly ChannelId Id = new("Email");

    /// <summary>The descriptor of the email channel, with languages and rich text (D77).</summary>
    public static readonly ChannelDescriptor<EmailChannelSettings> Descriptor =
        new(Id, "Email", ChannelFeatures.Languages | ChannelFeatures.RichText);

    internal const string SubjectPart = "subject";

    internal const string BodyPart = "body";

    /// <summary>The template parts that an email reads (D215, D220): the subject, the html body, and the text body.</summary>
    internal static readonly TemplatePart[] TemplateParts =
        [new(SubjectPart, TemplatePartKind.PlainText), new(BodyPart, TemplatePartKind.Html), new(BodyPart, TemplatePartKind.PlainText)];
}
