using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;

namespace MsgHatch;

/// <summary>
/// The HTTP status of each error type, and the error of an HTTP failure (D233). The <c>.AspNetCore</c> package and the
/// client package of each management API compile this file, so no public type has an HTTP status, and the domain gets
/// no ASP.NET Core reference.
/// </summary>
internal static class ErrorProblems
{
    /// <summary>Gives the status of a type: 400, 403, 404, 409, 410, 422, 429, or 503, and 500 for each other type.</summary>
    public static int StatusCodeOf(ErrorType type) =>
        type == Errors.ValidationFailed ? 400
        : type == Errors.Rejected ? 403
        : type == Errors.NotFound ? 404
        : type == Errors.Conflict ? 409
        : type == Errors.Expired ? 410
        : type == Errors.TemplateInvalid ? 422
        : type == Errors.RateLimited ? 429
        : type == Errors.Unavailable ? 503
        : 500;

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
