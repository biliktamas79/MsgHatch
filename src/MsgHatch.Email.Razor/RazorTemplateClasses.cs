using System.Reflection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Razor;

/// <summary>
/// The classes of the Razor email templates of one assembly and root namespace (D198), as a singleton. The Razor SDK
/// compiles <c>{MessageType}.{Language}.body.razor</c> into the class <c>{MessageType}_{Language}_body</c>, with <c>_</c>
/// for <c>-</c>, and a company folder into a part of the namespace (<see cref="ScopeFolders.SegmentOf"/>). The first
/// lookup scans the assembly one time for the classes that inherit <see cref="RazorEmailComponent{TPayload}"/>. A lookup
/// compares the names exactly, as a file store on Linux does, so two company ids that differ only in case never share a
/// template.
/// </summary>
internal sealed class RazorTemplateClasses(Assembly assembly, string rootNamespace)
{
    private readonly Lazy<Dictionary<string, Type>> _classes = new(() => assembly.GetTypes().Where(IsTemplate).ToDictionary(static type => type.FullName!));

    public string RootNamespace => rootNamespace;

    /// <summary>Gives the template class of a message type, a language, and a scope, or null when none exists.</summary>
    public Type? Find(MessageType type, string language, string? scope)
    {
        var className = $"{type.Name}_{language.Replace('-', '_')}_body";
        var name = scope is null ? $"{rootNamespace}.{className}"
            : ScopeFolders.SegmentOf(scope) is { } segment ? $"{rootNamespace}.{segment}.{className}"
            : null;
        return name is not null && _classes.Value.TryGetValue(name, out var found) ? found : null;
    }

    private static bool IsTemplate(Type type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(RazorEmailComponent<>))
            {
                return true;
            }
        }

        return false;
    }
}
