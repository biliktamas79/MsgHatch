namespace MsgHatch.Domain.Failures;

/// <summary>The rule that each client applies to its <see cref="IExceptionClassifier{TClient}"/> registrations (D141).</summary>
public static class ExceptionClassifiers
{
    /// <summary>Asks the classifiers in their order, and gives the first error type that is not null.</summary>
    /// <typeparam name="TClient">The client that uses the classifiers.</typeparam>
    /// <param name="classifiers">The classifiers, in the order of their registration.</param>
    /// <param name="exception">The exception of the library.</param>
    /// <returns>The error type of the first classifier that knows the exception, or null when none knows it.</returns>
    public static ErrorType? Classify<TClient>(this IEnumerable<IExceptionClassifier<TClient>> classifiers, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(classifiers);
        foreach (var classifier in classifiers)
        {
            if (classifier.Classify(exception) is { } type)
            {
                return type;
            }
        }

        return null;
    }
}
