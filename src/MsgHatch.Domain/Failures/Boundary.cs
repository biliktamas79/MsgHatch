namespace MsgHatch.Domain.Failures;

/// <summary>
/// The outer boundary of a public entry point (section 4.12): it turns a cancellation into
/// <see cref="Errors.OperationCancelled"/>, and an unexpected exception of consumer code into <see cref="Errors.Exception"/>.
/// </summary>
internal static class Boundary
{
    public static async ValueTask<ErrorOr<T>> RunAsync<T>(Func<ValueTask<ErrorOr<T>>> run, CancellationToken ct)
    {
        try
        {
            return await run();
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
