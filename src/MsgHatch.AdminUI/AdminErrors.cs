using MsgHatch.Domain.Failures;

namespace MsgHatch.AdminUI;

/// <summary>The text that an admin page shows for an error (D239).</summary>
public static class AdminErrors
{
    /// <summary>The text of <see cref="Errors.Rejected"/>: the signed-in user has no permission.</summary>
    public const string RejectedText = "You have no permission to do this action.";

    /// <summary>The text of <see cref="Errors.Unavailable"/>: the API is not available.</summary>
    public const string UnavailableText = "The API is not available. Try again later.";

    /// <summary>The text of <see cref="Errors.Conflict"/>: the page loads the stored version again.</summary>
    public const string ConflictText = "Another person changed this item. The page shows the stored version now.";

    /// <summary>
    /// Gives the text of an error: a fixed text for <see cref="Errors.Rejected"/>, <see cref="Errors.Unavailable"/>, and
    /// <see cref="Errors.Conflict"/>, and the message of each other error, or the name of its type when it has no message.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>The text for the user.</returns>
    public static string TextOf(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Type == Errors.Rejected ? RejectedText
            : error.Type == Errors.Unavailable ? UnavailableText
            : error.Type == Errors.Conflict ? ConflictText
            : string.IsNullOrWhiteSpace(error.Message) ? error.Type.Name
            : error.Message;
    }
}
