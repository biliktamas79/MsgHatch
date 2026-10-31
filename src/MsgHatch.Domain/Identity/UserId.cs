using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Identity;

/// <summary>
/// The id of a user, as text (D73, D114). Two ids are equal when their texts are equal with ordinal comparison.
/// The text is not empty, has at most 256 characters, and has no control characters.
/// </summary>
public readonly record struct UserId
{
    /// <summary>Makes an id from a string id as it is.</summary>
    /// <param name="value">The text of the id.</param>
    /// <exception cref="ArgumentException">The text is not a valid id. This is a programmer error.</exception>
    public UserId(string value)
    {
        Value = StringIds.Validate(value, nameof(value));
    }

    /// <summary>The text of the id.</summary>
    public string Value { get; }

    /// <summary>Checks a string from outside, for example from an HTTP request.</summary>
    /// <param name="value">The text of the id.</param>
    /// <returns>The id, or <see cref="Errors.ValidationFailed"/> for an invalid text.</returns>
    public static ErrorOr<UserId> Create(string? value) =>
        StringIds.IsValid(value)
            ? new UserId(value)
            : new Error(Errors.ValidationFailed, "An id is not empty, has at most 256 characters, and has no control characters.");

    /// <summary>Maps an id of the consumer, for example an <see cref="int"/> or a <see cref="Guid"/>, with the invariant culture.</summary>
    /// <typeparam name="T">The type of the id of the consumer.</typeparam>
    /// <param name="id">The id of the consumer.</param>
    /// <returns>The id with the text of the default format of <typeparamref name="T"/>.</returns>
    public static UserId From<T>(T id) where T : ISpanFormattable => new(StringIds.Format(id));

    /// <summary>Parses the text back with the invariant culture.</summary>
    /// <typeparam name="T">The type of the id of the consumer.</typeparam>
    /// <param name="id">The parsed id.</param>
    /// <returns>False when the text is not a valid <typeparamref name="T"/>.</returns>
    public bool TryAs<T>([MaybeNullWhen(false)] out T id) where T : ISpanParsable<T> =>
        T.TryParse(Value, CultureInfo.InvariantCulture, out id);
}
