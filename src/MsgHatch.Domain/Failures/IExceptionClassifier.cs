namespace MsgHatch.Domain.Failures;

/// <summary>
/// Classifies an exception of the external library of one client (D141), for example an exception of a Wolverine
/// transport package that the client package does not reference. The type argument is the client that asks the
/// classifier, so DI gives each client only its own classifiers. The client asks its classifiers before its built-in
/// mapping, except for a cancelled operation.
/// </summary>
/// <typeparam name="TClient">The client that uses the classifier, for example <c>WolverineBrokerClient</c>.</typeparam>
public interface IExceptionClassifier<TClient>
{
    /// <summary>Gives the error type of an exception. A non-transient type stops the retries of the message.</summary>
    /// <param name="exception">The exception of the library.</param>
    /// <returns>The error type, or null when this classifier does not know the exception.</returns>
    ErrorType? Classify(Exception exception);
}
