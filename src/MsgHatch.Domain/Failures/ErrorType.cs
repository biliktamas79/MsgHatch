namespace MsgHatch.Domain.Failures;

/// <summary>
/// The kind of an <see cref="Error"/>. The static class <see cref="Errors"/> holds each kind of MsgHatch. The code is the
/// identity of a kind, so two kinds with one code are equal, and the name and the transient flag are data about the code
/// (D238).
/// </summary>
/// <param name="Code">The stable numeric code of the kind.</param>
/// <param name="Name">The stable name of the kind.</param>
/// <param name="IsTransient">True when a later try of the same operation can succeed.</param>
public sealed record ErrorType(int Code, string Name, bool IsTransient)
{
    /// <summary>Compares two kinds by their code only (D238).</summary>
    /// <param name="other">The other kind.</param>
    /// <returns>True when the other kind has the same code.</returns>
    public bool Equals(ErrorType? other) => other is not null && Code == other.Code;

    /// <summary>Gives the hash of the code only (D238).</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => Code;
}
