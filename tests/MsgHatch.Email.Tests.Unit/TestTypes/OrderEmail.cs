using MsgHatch.Email.Placeholders;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>The placeholder payload of the engine tests. <c>Code_1</c> has a name with an underscore and a digit.</summary>
[PlaceholderPayload]
public sealed partial record OrderEmail(string Name, int Count, decimal Total, DateTime ShippedAt, string? Note = null, string? Code_1 = null);
