using System.Text.Json.Serialization;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Data.FileSystem;

/// <summary>
/// The JSON type information of the down state file <c>{channel}/down.json</c> (D106) and of the registry entry files
/// (section 4.20), with enum values as strings (D23).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    Converters = [typeof(ChannelIdJsonConverter), typeof(MessageTypeJsonConverter)])]
[JsonSerializable(typeof(ChannelDown))]
[JsonSerializable(typeof(ChannelEntry))]
[JsonSerializable(typeof(MessageTypeEntry))]
internal sealed partial class FileStoreJsonContext : JsonSerializerContext;
