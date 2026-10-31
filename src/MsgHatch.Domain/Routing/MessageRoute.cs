using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>One delivery target on one channel (D95). The outbox gets one outgoing message for each route.</summary>
/// <param name="Target">The delivery target.</param>
/// <param name="Channel">The channel.</param>
/// <param name="Settings">The settings, of the settings type of the channel.</param>
/// <param name="Language">A culture name that <see cref="System.Globalization.CultureInfo.GetCultureInfo(string)"/> knows.</param>
/// <param name="Address">The channel address of the target, or null.</param>
/// <param name="TemplateScope">The key of a template override, or null.</param>
/// <param name="Trace">The trace that tells why the router gave the route, or null.</param>
public readonly record struct MessageRoute(
    DeliveryTarget Target, ChannelId Channel, ChannelSettings Settings, string Language, string? Address,
    string? TemplateScope, RouteTrace? Trace);
