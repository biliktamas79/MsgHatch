using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Pipeline;

/// <summary>The values that each outgoing message of one event shares: the time, the trace context, and the retry options.</summary>
internal readonly record struct OutgoingDefaults(DateTime NowUtc, string? TraceParent, IOptionsMonitor<RetryOptions> RetryOptions);
