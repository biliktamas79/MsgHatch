namespace MsgHatch.Templates.Liquid.AdminUI;

/// <summary>The kind of a span of a Liquid text (D242).</summary>
public enum LiquidSpanKind
{
    /// <summary>The plain text outside of the tags.</summary>
    Text,

    /// <summary>A delimiter <c>{{</c>, <c>}}</c>, <c>{%</c>, or <c>%}</c>, with its <c>-</c> mark.</summary>
    Delimiter,

    /// <summary>The name of a tag, for example <c>if</c>.</summary>
    TagName,

    /// <summary>The name of a filter after <c>|</c>, for example <c>upcase</c>.</summary>
    FilterName,

    /// <summary>A text in single or double quotes.</summary>
    Quoted,

    /// <summary>A number.</summary>
    Number,

    /// <summary>Each other part of the code in a tag, for example a variable, an operator, or a space.</summary>
    Code,

    /// <summary>The text between <c>{% comment %}</c> and <c>{% endcomment %}</c>.</summary>
    Comment,

    /// <summary>The text between <c>{% raw %}</c> and <c>{% endraw %}</c>.</summary>
    Raw,
}
