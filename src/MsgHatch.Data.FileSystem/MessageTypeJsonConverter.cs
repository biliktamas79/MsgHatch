using System.Text.Json;
using System.Text.Json.Serialization;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Data.FileSystem;

/// <summary>Writes a <see cref="MessageType"/> as its name, because its property <c>Name</c> has no setter.</summary>
internal sealed class MessageTypeJsonConverter : JsonConverter<MessageType>
{
    public override MessageType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonNames.Read(reader.GetString(), static name => new MessageType(name));

    public override void Write(Utf8JsonWriter writer, MessageType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Name);
}
