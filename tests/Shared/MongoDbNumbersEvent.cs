using System.Text.Json;
using System.Text.Json.Serialization;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Tests.Shared;

/// <summary>
/// An event with numbers of each kind, for the number rule of the BSON conversion (D184). <c>Directory.Build.props</c> links
/// this file into the unit and the integration test project of <c>MsgHatch.Data.MongoDb</c>.
/// </summary>
internal sealed record NumbersEvent(int Small, long Large, decimal Money, double Ratio, double Tiny, double Huge, JsonElement? Raw);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NumbersEvent))]
internal sealed partial class NumbersJsonContext : JsonSerializerContext;

internal static class NumbersContentTypes
{
    public static ContentTypeEntry NumbersEventV1 { get; } = ContentTypeEntry.ForReceivedMessage("NumbersEvent", 1, NumbersJsonContext.Default.NumbersEvent);
}
