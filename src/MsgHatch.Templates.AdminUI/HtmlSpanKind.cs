namespace MsgHatch.Templates.AdminUI;

/// <summary>The kind of a span of an HTML text (D252).</summary>
public enum HtmlSpanKind
{
    /// <summary>The text outside of the tags, and the spaces between the attributes of a tag.</summary>
    Text,

    /// <summary>A mark of a tag: <c>&lt;</c>, <c>&lt;/</c>, <c>&lt;!</c>, <c>&gt;</c>, <c>/&gt;</c>, or the <c>=</c> of an attribute.</summary>
    Markup,

    /// <summary>The name of a tag, for example <c>p</c>.</summary>
    TagName,

    /// <summary>The name of an attribute, for example <c>href</c>.</summary>
    AttributeName,

    /// <summary>The value of an attribute, with its quotes.</summary>
    AttributeValue,

    /// <summary>A comment, from <c>&lt;!--</c> to <c>--&gt;</c>.</summary>
    Comment,

    /// <summary>A character reference, for example <c>&amp;amp;</c> or <c>&amp;#160;</c>.</summary>
    Entity,
}
