using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Domain.Failures;

/// <summary>An expected failure as a value. MsgHatch returns it, and does not throw it.</summary>
/// <param name="Type">The kind of the failure.</param>
/// <param name="Message">An optional text for a person. Code reads <paramref name="Type"/>, never this text.</param>
/// <param name="Exception">The exception of external code that caused the failure, if any.</param>
[SuppressMessage("Naming", "CA1716", Justification = "Section 4.4 of the design fixes the name Error.")]
public sealed record Error(ErrorType Type, string? Message = null, Exception? Exception = null)
{
    /// <summary>
    /// The wait that the provider asked for before the next try, for example from the header <c>Retry-After</c> of a
    /// rate limit, or null when the provider gave none. A dispatcher does not try a transient error again before this
    /// wait, and waits at most <c>RetryOptions.MaxDelay</c> (D306).
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }
}
