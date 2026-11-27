using MsgHatch.Domain.Failures;

namespace MsgHatch;

/// <summary>
/// The HTTP status of each error type (D233). The <c>.AspNetCore</c> package of each management API compiles this file, and
/// the client packages compile <c>ErrorProblems.Responses.cs</c>, so each package has only the half that it calls. So no
/// public type has an HTTP status, and the domain gets no ASP.NET Core reference.
/// </summary>
internal static partial class ErrorProblems
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
}
