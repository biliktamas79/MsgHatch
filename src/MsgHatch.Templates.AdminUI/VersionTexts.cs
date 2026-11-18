using System.Globalization;

namespace MsgHatch.Templates.AdminUI;

/// <summary>The text of a template version on a page (D267).</summary>
internal static class VersionTexts
{
    /// <summary>Gives the text that the store rendered, or the number when no store rendered it, for example for a new template.</summary>
    public static string Of(long version, string? versionText) =>
        string.IsNullOrEmpty(versionText) ? version.ToString(CultureInfo.InvariantCulture) : versionText;
}
