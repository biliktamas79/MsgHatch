using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;

namespace MsgHatch.AspNetCore;

/// <summary>
/// Writes an error of the registry and the delivery routes as problem details (D233): the status of
/// <see cref="ErrorProblems"/>, the error name as the title, the message as the detail, and the member <c>errorType</c>.
/// </summary>
internal static class ApiProblems
{
    public static ProblemHttpResult Problem(Error error) =>
        TypedResults.Problem(
            detail: error.Message,
            statusCode: ErrorProblems.StatusCodeOf(error.Type),
            title: error.Type.Name,
            extensions: new Dictionary<string, object?> { ["errorType"] = ErrorTypeJsonOf(error.Type) });

    private static JsonElement ErrorTypeJsonOf(ErrorType type) =>
        JsonSerializer.SerializeToElement(ErrorTypeBody.From(type), ApiErrorJson.Default.ErrorTypeBody);
}
