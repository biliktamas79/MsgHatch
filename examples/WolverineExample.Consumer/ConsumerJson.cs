using System.Text.Json.Serialization;
using MsgHatch.Channels.SignalR;
using WolverineExample.Consumer.Notifications;
using WolverineExample.Contracts;

namespace WolverineExample.Consumer;

/// <summary>The JSON type information of the events that the inbox stores, and of the envelope that the outbox stores.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UserLogin))]
[JsonSerializable(typeof(CompanyArchived))]
[JsonSerializable(typeof(ProjectCreated))]
[JsonSerializable(typeof(ProjectFinished))]
[JsonSerializable(typeof(CompanyMaintenance))]
[JsonSerializable(typeof(SignalREnvelope<Notification>))]
public sealed partial class ConsumerJson : JsonSerializerContext;
