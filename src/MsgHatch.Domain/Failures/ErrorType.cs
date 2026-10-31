namespace MsgHatch.Domain.Failures;

/// <summary>The kind of an <see cref="Error"/>. The static class <see cref="Errors"/> holds each kind.</summary>
/// <param name="Code">The stable numeric code of the kind.</param>
/// <param name="Name">The stable name of the kind.</param>
/// <param name="IsTransient">True when a later try of the same operation can succeed.</param>
public sealed record ErrorType(int Code, string Name, bool IsTransient);
