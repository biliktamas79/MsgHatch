namespace MsgHatch.Templates.AdminUI;

internal sealed record TemplateEditorRegistration(string Engine, Type EditorType);

internal sealed class TemplateEditors(IEnumerable<TemplateEditorRegistration> registrations)
{
    private readonly Dictionary<string, Type> _editors = EditorsByEngine(registrations);

    public Type EditorOf(IReadOnlyList<string> engines) =>
        engines.Count == 1 && _editors.TryGetValue(engines[0], out var editor) ? editor : typeof(PlainTemplateEditor);

    private static Dictionary<string, Type> EditorsByEngine(IEnumerable<TemplateEditorRegistration> registrations)
    {
        var editors = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            editors[registration.Engine] = registration.EditorType;
        }

        return editors;
    }
}
