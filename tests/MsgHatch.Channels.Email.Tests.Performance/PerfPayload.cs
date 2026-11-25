using MsgHatch.Templates.Placeholders;

namespace MsgHatch.Channels.Email.Tests.Performance;

/// <summary>The payload of the email benchmarks, with a text property and two value-type properties.</summary>
/// <param name="Name">The name of the customer.</param>
/// <param name="Count">The number of items.</param>
/// <param name="Amount">The total amount.</param>
[PlaceholderPayload]
public sealed partial record PerfPayload(string Name, int Count, decimal Amount);
