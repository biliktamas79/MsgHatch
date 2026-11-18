using System.Text.Json;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Api.Contracts;

/// <summary>
/// The settings of a channel that the content type registry of the host has no settings type for (D229). A response
/// gives the JSON of the settings with no change, and a request sends it back with no change. The server still gives
/// <c>Errors.ValidationFailed</c> for the settings of a channel with no settings type.
/// </summary>
/// <param name="Json">The JSON of the settings.</param>
public sealed record JsonChannelSettings(JsonElement Json) : ChannelSettings;
