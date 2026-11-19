namespace MsgHatch.Domain.Failures;

/// <summary>
/// The outer boundary of a public entry point (section 4.12): it turns a cancellation into
/// <see cref="Errors.OperationCancelled"/>, and an unexpected exception of consumer code into <see cref="Errors.Exception"/>.
/// </summary>
internal static class Boundary
{
    public static ValueTask<ErrorOr<T>> RunAsync<T>(Func<ValueTask<ErrorOr<T>>> run, CancellationToken ct) =>
        RunAsync(static run => run(), run, ct);

    /// <summary>Runs <paramref name="run"/> with <paramref name="state"/>, so that a caller with a static lambda allocates no closure.</summary>
    public static async ValueTask<ErrorOr<T>> RunAsync<T, TState>(Func<TState, ValueTask<ErrorOr<T>>> run, TState state, CancellationToken ct)
    {
        try
        {
            return await run(state);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new Error(Errors.OperationCancelled);
        }
        catch (Exception exception)
        {
            return new Error(Errors.Exception, exception.Message, exception);
        }
    }
}
