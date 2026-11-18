using MsgHatch.Domain.Failures;

namespace MsgHatch.Api.Contracts;

/// <summary>
/// The wire form of an <see cref="ErrorType"/>: the extension member <c>errorType</c> of the problem details of an HTTP
/// API (D233). The code is the identity of the type (D238).
/// </summary>
/// <param name="Code">The code of the type.</param>
/// <param name="Name">The name of the type.</param>
/// <param name="IsTransient">True when a later try of the same operation can succeed.</param>
public sealed record ErrorTypeBody(int Code, string Name, bool IsTransient)
{
    /// <summary>Gives the wire form of a type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The type is null.</exception>
    public static ErrorTypeBody From(ErrorType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new ErrorTypeBody(type.Code, type.Name, type.IsTransient);
    }

    /// <summary>
    /// Gives the type of <see cref="Errors.Find"/> for a known code, so <c>body.ToErrorType() == Errors.NotFound</c> is
    /// true with the local name and flag, and a new type of the body for an unknown code, for example a type of the
    /// consumer (D238).
    /// </summary>
    /// <returns>The type.</returns>
    public ErrorType ToErrorType() => Errors.Find(Code) ?? new ErrorType(Code, Name, IsTransient);
}
