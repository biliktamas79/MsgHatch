using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.AdminUI;

/// <summary>Finds the declared part of a lookup, with its engines and its sample flag (D240, D254).</summary>
internal static class TemplateParts
{
    /// <summary>Gives the declared part with the name and the kind of the lookup, or null for a part that no registration declares.</summary>
    public static TemplatePartInfo? Of(IReadOnlyList<TemplatePartInfo> parts, TemplateLookup lookup) =>
        parts.FirstOrDefault(part => part.Part == new TemplatePart(lookup.Part, lookup.Kind));
}
