using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// What the setup methods registered: one instance in the service collection. The setup methods fill it before
/// the container is built, and the container only reads it.
/// </summary>
internal sealed class MsgHatchSetup(string configSectionPath)
{
    public string ConfigSectionPath { get; } = configSectionPath;

    public List<string> OtherConfigSectionPaths { get; } = [];

    public List<ChannelDescriptor> Channels { get; } = [];

    public List<ContentTypeEntry> ContentTypes { get; } = [];

    public List<IMessageHandler> Messages { get; } = [];

    public List<ImplementationSelection> Selections { get; } = [];

    public List<ImplementationSelection> SharedOptionsValues { get; } = [];

    public HashSet<Type> TenantServiceTypes { get; } = [];

    public HashSet<ChannelId> ChannelsWithOptions { get; } = [];

    public IOpenRouterFactory? OpenRouterFactory { get; set; }

    public void Select(Type service, string setupMethod) => Selections.Add(new ImplementationSelection(service, setupMethod));

    public string SectionPathOf(string sectionKey) => $"{ConfigSectionPath}:{sectionKey}";

    public void UseConfigSectionPath(string? configSectionPath)
    {
        if (configSectionPath is not null && !string.Equals(configSectionPath, ConfigSectionPath, StringComparison.OrdinalIgnoreCase))
        {
            OtherConfigSectionPaths.Add(configSectionPath);
        }
    }
}

internal readonly record struct ImplementationSelection(Type Service, string SetupMethod);
