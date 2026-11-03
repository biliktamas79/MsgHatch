using System.Text.Json.Serialization;
using MsgHatch.Domain.Dispatch;

namespace MsgHatch.Data.FileSystem;

/// <summary>The JSON type information of the down state file <c>{channel}/down.json</c> (D106), with enum values as strings (D23).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, Converters = [typeof(ChannelIdJsonConverter)])]
[JsonSerializable(typeof(ChannelDown))]
internal sealed partial class FileStoreJsonContext : JsonSerializerContext;
