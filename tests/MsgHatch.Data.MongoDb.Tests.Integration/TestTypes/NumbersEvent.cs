using System.Text.Json;
using System.Text.Json.Serialization;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;

/// <summary>An event with numbers of each kind, for the number rule of the BSON conversion (D184).</summary>
public sealed record NumbersEvent(int Small, long Large, decimal Money, double Ratio, double Tiny, double Huge, JsonElement? Raw);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NumbersEvent))]
public sealed partial class NumbersJsonContext : JsonSerializerContext;

public static class NumbersContentTypes
{
    public static ContentTypeEntry NumbersEventV1 { get; } = ContentTypeEntry.ForReceivedMessage("NumbersEvent", 1, NumbersJsonContext.Default.NumbersEvent);
}
