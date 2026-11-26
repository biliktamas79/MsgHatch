using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Failures;

/// <summary>The rule of the exception classifiers of a client (D141): the first result that is not null wins.</summary>
public sealed class ExceptionClassifiersUnitTests
{
    public static TheoryData<string, ErrorType?[], int?, int[]> ClassifyRows => new()
    {
        { "No classifier", [], null, [] },
        { "Only classifiers that do not know the exception", [null, null], null, [1, 1] },
        { "The first classifier that knows the exception wins", [null, Errors.Conflict, Errors.Rejected], 409, [1, 1, 0] },
    };

    [Theory]
    [MemberData(nameof(ClassifyRows))]
    public void Classify_ClassifiersInTheirOrder_GivesTheFirstErrorTypeThatIsNotNull(
        string row, ErrorType?[] results, int? expectedCode, int[] expectedCalls)
    {
        // Arrange
        IExceptionClassifier<TestClient>[] classifiers = [.. results.Select(AClassifier)];
        var exception = new IOException();

        // Act
        var type = classifiers.Classify(exception);

        // Assert
        Assert.Equal((row, expectedCode), (row, type?.Code));
        int[] calls = [.. classifiers.Select(c => c.ReceivedCalls().Count(call => ReferenceEquals(call.GetArguments()[0], exception)))];
        Assert.Equal(expectedCalls, calls);
    }

    private static IExceptionClassifier<TestClient> AClassifier(ErrorType? result)
    {
        var classifier = Substitute.For<IExceptionClassifier<TestClient>>();
        classifier.Classify(default!).ReturnsForAnyArgs(result);
        return classifier;
    }
}
