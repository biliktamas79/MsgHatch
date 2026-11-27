using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;

namespace MsgHatch;

/// <summary>
/// The error of an HTTP failure (D233). The client package of each management API compiles this file, and the code of the
/// generator calls it. The <c>.AspNetCore</c> packages compile <c>ErrorProblems.cs</c>.
/// </summary>
internal static partial class ErrorProblems
{
    /// <summary>
    /// Gives the error of a failed response: the type of <paramref name="errorType"/>, or for a body with no
    /// <c>errorType</c>, the type of the status, and <paramref name="detail"/> as the message.
    /// </summary>
    public static Error ErrorOf(int statusCode, ErrorTypeBody? errorType, string? detail) =>
        new(errorType?.ToErrorType() ?? TypeOfAResponseFromTheHost(statusCode), detail);

    /// <summary>A body with no <c>errorType</c> comes from the host, a proxy, or the framework, so a 404 of a wrong route is no <c>NotFound</c>.</summary>
    private static ErrorType TypeOfAResponseFromTheHost(int statusCode) => statusCode switch
    {
        401 or 403 => Errors.Rejected,
        429 => Errors.RateLimited,
        408 or 502 or 503 or 504 => Errors.Unavailable,
        _ => Errors.Exception,
    };
}
