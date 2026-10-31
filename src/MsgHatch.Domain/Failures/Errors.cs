namespace MsgHatch.Domain.Failures;

/// <summary>The error kinds of MsgHatch.</summary>
public static class Errors
{
    /// <summary>Invalid input or configuration at run time.</summary>
    public static readonly ErrorType ValidationFailed = new(400, nameof(ValidationFailed), false);

    /// <summary>A provider rejected the message.</summary>
    public static readonly ErrorType Rejected = new(403, nameof(Rejected), false);

    /// <summary>No template, or no registration for a type.</summary>
    public static readonly ErrorType NotFound = new(404, nameof(NotFound), false);

    /// <summary>A duplicate, or an update with a revision that is not the stored revision.</summary>
    public static readonly ErrorType Conflict = new(409, nameof(Conflict), false);

    /// <summary>An outgoing message after its expiry.</summary>
    public static readonly ErrorType Expired = new(410, nameof(Expired), false);

    /// <summary>A stored record that the code cannot read.</summary>
    public static readonly ErrorType SerializationFailed = new(415, nameof(SerializationFailed), false);

    /// <summary>A template with a syntax error or an unknown placeholder.</summary>
    public static readonly ErrorType TemplateInvalid = new(422, nameof(TemplateInvalid), false);

    /// <summary>A stored version that is newer than the code. A newer instance can read it.</summary>
    public static readonly ErrorType UnsupportedVersion = new(426, nameof(UnsupportedVersion), true);

    /// <summary>A provider limit.</summary>
    public static readonly ErrorType RateLimited = new(429, nameof(RateLimited), true);

    /// <summary>A cancelled operation. No try is recorded for it.</summary>
    public static readonly ErrorType OperationCancelled = new(460, nameof(OperationCancelled), true);

    /// <summary>A <c>default</c> <see cref="ErrorOr{T}"/>.</summary>
    public static readonly ErrorType Uninitialized = new(499, nameof(Uninitialized), false);

    /// <summary>An unexpected exception at a boundary.</summary>
    public static readonly ErrorType Exception = new(500, nameof(Exception), true);

    /// <summary>A provider or a network that is not available.</summary>
    public static readonly ErrorType Unavailable = new(503, nameof(Unavailable), true);
}
