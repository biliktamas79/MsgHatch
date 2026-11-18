using MsgHatch.Domain.Failures;

namespace MsgHatch;

/// <summary>
/// Maps the result of a generated HTTP call to a domain value with the mapping of a contract record (D228, D236). A null
/// item of a list is a body that the contract does not allow, so it gives <see cref="Errors.SerializationFailed"/>. The
/// client packages compile this file.
/// </summary>
internal static class ApiResults
{
    public static ErrorOr<T> One<TBody, T>(ErrorOr<TBody> read, Func<TBody, ErrorOr<T>> map) => read.IsError ? read.Error : map(read.Value);

    public static ErrorOr<IReadOnlyList<T>> All<TBody, T>(ErrorOr<IReadOnlyList<TBody>> read, Func<TBody, ErrorOr<T>> map)
        where TBody : class
    {
        if (read.IsError)
        {
            return read.Error;
        }

        var values = new T[read.Value.Count];
        for (var index = 0; index < values.Length; index++)
        {
            var value = read.Value[index] is { } body ? map(body) : new Error(Errors.SerializationFailed, "A list of the response has a null item.");
            if (value.IsError)
            {
                return value.Error;
            }

            values[index] = value.Value;
        }

        return values;
    }
}
