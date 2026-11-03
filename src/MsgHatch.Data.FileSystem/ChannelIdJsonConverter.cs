using System.Text.Json;
using System.Text.Json.Serialization;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Data.FileSystem;

/// <summary>Writes a <see cref="ChannelId"/> as its name, because its property <c>Name</c> has no setter.</summary>
internal sealed class ChannelIdJsonConverter : JsonConverter<ChannelId>
{
    public override ChannelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("The channel of a down state is null."));

    public override void Write(Utf8JsonWriter writer, ChannelId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Name);
}
