using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Domain.Failures;

/// <summary>
/// The result of an operation: a value or an <see cref="Failures.Error"/>.
/// A <c>default</c> value is an error with <see cref="Errors.Uninitialized"/>, so a forgotten value never looks like a success.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public readonly struct ErrorOr<T>
{
    private readonly T _value;
    private readonly Error? _error;
    private readonly bool _hasValue;

    private ErrorOr(T value)
    {
        _value = value;
        _error = null;
        _hasValue = true;
    }

    private ErrorOr(Error error)
    {
        _value = default!;
        _error = error;
        _hasValue = false;
    }

    /// <summary>The value. It is the default of <typeparamref name="T"/> when <see cref="IsError"/> is true.</summary>
    public T Value => _value;

    /// <summary>The error, or null for a success. The error of a <c>default</c> value is a new <see cref="Errors.Uninitialized"/> error.</summary>
    public Error? Error => _hasValue ? null : _error ?? new Error(Errors.Uninitialized);

    /// <summary>True when the result is an error.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsError => !_hasValue;

    /// <summary>True when the result is a value.</summary>
    public bool IsSuccess => _hasValue;

    /// <summary>Makes a successful result.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The result with the value.</returns>
    [SuppressMessage("Design", "CA1000", Justification = "Section 4.4 of the design fixes ErrorOr<T>.ForValue.")]
    public static ErrorOr<T> ForValue(T value) => new(value);

    /// <summary>Makes a failed result.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The result with the error.</returns>
    [SuppressMessage("Design", "CA1000", Justification = "Section 4.4 of the design fixes ErrorOr<T>.ForError.")]
    public static ErrorOr<T> ForError(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }

    /// <summary>Makes a successful result.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator ErrorOr<T>(T value) => ForValue(value);

    /// <summary>Makes a failed result.</summary>
    /// <param name="error">The error.</param>
    public static implicit operator ErrorOr<T>(Error error) => ForError(error);

    /// <summary>
    /// Calls <paramref name="onSuccess"/> with the value of a success, or gives the error of a failure unchanged.
    /// Give a static lambda, so that no closure is allocated.
    /// </summary>
    /// <typeparam name="T2">The type of the next value.</typeparam>
    /// <param name="onSuccess">The function for the value.</param>
    /// <returns>The result of <paramref name="onSuccess"/>, or the error.</returns>
    public ErrorOr<T2> ThenIfSuccess<T2>(Func<T, ErrorOr<T2>> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        return IsError ? ErrorOr<T2>.ForError(Error) : onSuccess(_value);
    }
}
