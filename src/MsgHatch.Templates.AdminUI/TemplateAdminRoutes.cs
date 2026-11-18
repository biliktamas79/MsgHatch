namespace MsgHatch.Templates.AdminUI;

/// <summary>The addresses of the pages of the template admin UI, so that a host can link to a page (D239).</summary>
public static class TemplateAdminRoutes
{
    /// <summary>The address of the template list.</summary>
    public const string List = "/msghatch/templates";

    internal const string EditTemplate = List + "/{TypeName}/{Language}/{Part}/{KindName}";

    internal const string ViewTemplate = EditTemplate + "/view";

    /// <summary>Gives the address of the edit page of one part of a template.</summary>
    /// <param name="lookup">The message type, the language, the template scope, the part, and the kind.</param>
    /// <returns>For example <c>/msghatch/templates/Requests/en/subject/PlainText?companyId=42</c>.</returns>
    public static string Edit(TemplateLookup lookup) => AddressOf(lookup, string.Empty);

    /// <summary>Gives the address of the read-only view page of one part of a template (D257).</summary>
    /// <param name="lookup">The message type, the language, the template scope, the part, and the kind.</param>
    /// <returns>For example <c>/msghatch/templates/Requests/en/subject/PlainText/view?companyId=42</c>.</returns>
    public static string View(TemplateLookup lookup) => AddressOf(lookup, "/view");

    private static string AddressOf(TemplateLookup lookup, string page)
    {
        var path = $"{List}/{Uri.EscapeDataString(lookup.Type.Name)}/{Uri.EscapeDataString(lookup.Language)}/{Uri.EscapeDataString(lookup.Part)}/{lookup.Kind}{page}";
        return lookup.Scope is { } scope ? $"{path}?companyId={Uri.EscapeDataString(scope)}" : path;
    }
}
