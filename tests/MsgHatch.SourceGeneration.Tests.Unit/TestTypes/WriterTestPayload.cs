using MsgHatch.Email.Placeholders;

namespace MsgHatch.SourceGeneration.Tests.Unit.TestTypes;

/// <summary>The payload of the writer tests: one property of each kind that the generator writes.</summary>
[PlaceholderPayload]
public sealed partial record WriterTestPayload(
    string Name, string? Note, int Count, int? Rank, DateTime ShippedAt, Guid OrderId, bool Express, object? Extra);
