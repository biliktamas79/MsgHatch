namespace MsgHatch.Email;

/// <summary>The format of an email.</summary>
public enum EmailFormat
{
    /// <summary>The html body, and the text body when the template has one. It needs the feature <c>RichText</c>.</summary>
    Html,

    /// <summary>Only the text body.</summary>
    PlainText,
}
