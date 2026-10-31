using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// What the setup methods registered: one instance in the service collection. The setup methods fill it before
/// the container is built, and the container only reads it.
/// </summary>
internal sealed class MsgHatchSetup
{
    public List<ChannelDescriptor> Channels { get; } = [];

    public List<ContentTypeEntry> ContentTypes { get; } = [];

    public List<IMessageHandler> Messages { get; } = [];

    public List<ImplementationSelection> Selections { get; } = [];

    public HashSet<Type> TenantServiceTypes { get; } = [];

    public Type? OpenRouterType { get; set; }

    public void Select(Type service, string setupMethod) => Selections.Add(new ImplementationSelection(service, setupMethod));
}

internal readonly record struct ImplementationSelection(Type Service, string SetupMethod);
