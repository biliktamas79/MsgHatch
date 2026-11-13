using System.Text.Json;
using System.Text.Json.Serialization;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Data.FileSystem;

/// <summary>Writes a <see cref="ChannelId"/> as its name, because its property <c>Name</c> has no setter.</summary>
internal sealed class ChannelIdJsonConverter : JsonConverter<ChannelId>
{
    public override ChannelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonNames.Read(reader.GetString(), static name => new ChannelId(name));

    public override void Write(Utf8JsonWriter writer, ChannelId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Name);
}
