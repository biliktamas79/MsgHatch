using System.Text.Json.Serialization;
using MsgHatch.Domain.Management;

namespace MsgHatch.AspNetCore;

/// <summary>
/// The JSON type information of the request and response bodies of the delivery endpoints, with camelCase names and
/// enum values as strings (section 4.19). A missing required property is invalid JSON, so the endpoint gives 400 for it.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PauseRequest))]
[JsonSerializable(typeof(PurgeRequest))]
[JsonSerializable(typeof(InboxFilterRequest))]
[JsonSerializable(typeof(OutboxFilterRequest))]
[JsonSerializable(typeof(IReadOnlyList<ChannelStatusResponse>))]
[JsonSerializable(typeof(TrafficReportResponse))]
[JsonSerializable(typeof(MessagePageResponse<ReceivedMessageSummaryResponse>))]
[JsonSerializable(typeof(MessagePageResponse<OutgoingMessageSummaryResponse>))]
[JsonSerializable(typeof(OutgoingMessageDetailResponse))]
[JsonSerializable(typeof(DeliveryActionResult))]
[JsonSerializable(typeof(DeletedMessages))]
internal sealed partial class DeliveryEndpointJson : JsonSerializerContext;
