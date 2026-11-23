using MsgHatch.Domain.Failures;

namespace SignalRDemo;

/// <summary>The problem response of an error of a demo endpoint, with the name of its type as <c>errorType</c>.</summary>
public static class DemoProblems
{
    /// <summary>Gives the problem response of an error: 403 for a rejected request, 404 for a missing item, and 400 for each other error.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The problem response.</returns>
    public static IResult Of(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var status = error.Type == Errors.Rejected ? StatusCodes.Status403Forbidden
            : error.Type == Errors.NotFound ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
        return Results.Problem(error.Message, statusCode: status, extensions: new Dictionary<string, object?> { ["errorType"] = error.Type.Name });
    }
}
