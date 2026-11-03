using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// A classifier that gives <see cref="Errors.Conflict"/> for each exception. No built-in mapping of a client gives this
/// type, so a test sees that the client asked the classifier.
/// </summary>
/// <typeparam name="TClient">The client that uses the classifier.</typeparam>
public sealed class ConflictClassifier<TClient> : IExceptionClassifier<TClient>
{
    public ErrorType? Classify(Exception exception) => Errors.Conflict;
}

/// <summary>A classifier that knows no exception.</summary>
/// <typeparam name="TClient">The client that uses the classifier.</typeparam>
public sealed class UnknownClassifier<TClient> : IExceptionClassifier<TClient>
{
    public ErrorType? Classify(Exception exception) => null;
}

/// <summary>A client type for the classifier registrations of the domain tests.</summary>
public sealed class TestClient;

/// <summary>A second client type, to check that the classifiers of one client do not reach another client.</summary>
public sealed class OtherTestClient;
