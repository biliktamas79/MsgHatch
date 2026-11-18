using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace AdminUiExample.Components;

/// <summary>
/// A link of the top menu that is active on each page under its address, except on the pages of <see cref="Except"/>,
/// which have their own links in the menu.
/// </summary>
public sealed class SectionNavLink : NavLink
{
    /// <summary>The addresses under the address of the link that other links of the menu mark.</summary>
    [Parameter]
    public IReadOnlyList<string> Except { get; set; } = [];

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <inheritdoc />
    protected override bool ShouldMatch(string uriAbsolute) =>
        base.ShouldMatch(uriAbsolute) && !Except.Any(path => IsUnder(Navigation.ToBaseRelativePath(uriAbsolute), path.TrimStart('/')));

    private static bool IsUnder(string relativePath, string path) =>
        relativePath.StartsWith(path, StringComparison.OrdinalIgnoreCase)
        && (relativePath.Length == path.Length || relativePath[path.Length] is '/' or '?' or '#');
}
