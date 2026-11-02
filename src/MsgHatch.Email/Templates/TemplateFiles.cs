namespace MsgHatch.Email.Templates;

/// <summary>
/// The names of the template files of the library stores (section 4.10): <c>{MessageType}.{Language}[.{Scope}]</c>
/// with the part suffix <c>.subject.txt</c>, <c>.body.html</c>, or <c>.body.txt</c>. The language and the scope are
/// escaped with <see cref="Uri.EscapeDataString(string)"/>, so that a name never leaves its folder.
/// </summary>
internal static class TemplateFiles
{
    public static readonly (string Name, TemplatePartKind Kind, string Suffix)[] Parts =
    [
        ("subject", TemplatePartKind.PlainText, ".subject.txt"),
        ("html", TemplatePartKind.Html, ".body.html"),
        ("text", TemplatePartKind.PlainText, ".body.txt"),
    ];

    public static string BaseName(TemplateLookup lookup) =>
        lookup.Scope is null
            ? $"{lookup.Type.Name}.{Uri.EscapeDataString(lookup.Language)}"
            : $"{lookup.Type.Name}.{Uri.EscapeDataString(lookup.Language)}.{Uri.EscapeDataString(lookup.Scope)}";
}
