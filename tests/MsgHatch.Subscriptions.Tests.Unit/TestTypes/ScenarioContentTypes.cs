using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>
/// The content types of the message types A to E of the scenario, with the settings of the email, SignalR, and broker
/// channels, as the registrations of a host give them. The JSON contexts of the channel packages are internal, so only
/// a container gives the settings entries.
/// </summary>
public static class ScenarioContentTypes
{
    public static readonly IContentTypeRegistry Registry = ARegistry();

    public static readonly ChannelSettingsJson SettingsJson = new(Registry);

    private static IContentTypeRegistry ARegistry()
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddScenarioMessages();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IContentTypeRegistry>();
    }
}
