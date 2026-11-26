using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Api.Contracts.Tests.Unit;

/// <summary>The status of each error type, and the error of a failed response (D233). The test project compiles <c>src/Shared/ErrorProblems.cs</c>, as each package that uses it does.</summary>
public sealed class ErrorProblemsUnitTests
{
    public static TheoryData<string, int> StatusRows => new()
    {
        { nameof(Errors.ValidationFailed), 400 },
        { nameof(Errors.Rejected), 403 },
        { nameof(Errors.NotFound), 404 },
        { nameof(Errors.Conflict), 409 },
        { nameof(Errors.Expired), 410 },
        { nameof(Errors.SerializationFailed), 500 },
        { nameof(Errors.TemplateInvalid), 422 },
        { nameof(Errors.UnsupportedVersion), 500 },
        { nameof(Errors.RateLimited), 429 },
        { nameof(Errors.OperationCancelled), 500 },
        { nameof(Errors.Uninitialized), 500 },
        { nameof(Errors.Exception), 500 },
        { nameof(Errors.Unavailable), 503 },
    };

    public static TheoryData<int, string> NoErrorTypeRows => new()
    {
        { 401, nameof(Errors.Rejected) },
        { 403, nameof(Errors.Rejected) },
        { 429, nameof(Errors.RateLimited) },
        { 408, nameof(Errors.Unavailable) },
        { 502, nameof(Errors.Unavailable) },
        { 503, nameof(Errors.Unavailable) },
        { 504, nameof(Errors.Unavailable) },
        { 400, nameof(Errors.Exception) },
        { 404, nameof(Errors.Exception) },
        { 409, nameof(Errors.Exception) },
        { 500, nameof(Errors.Exception) },
    };

    [Fact]
    public void StatusRows_EachTypeOfErrors_HasARow()
    {
        // Act
        var names = StatusRows.Select(static row => row.Data.Item1);

        // Assert
        Assert.Equal(ErrorTypesOfErrors.All.Select(static type => type.Name).Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(StatusRows))]
    public void StatusCodeOf_TypeOfErrors_GivesTheStatusOfTheDesign(string name, int expected)
    {
        // Act
        var status = ErrorProblems.StatusCodeOf(ErrorTypesOfErrors.Named(name));

        // Assert
        Assert.Equal((name, expected), (name, status));
    }

    [Fact]
    public void StatusCodeOf_TypeOfTheConsumer_Gives500()
    {
        // Act
        var status = ErrorProblems.StatusCodeOf(new ErrorType(600, "OrderLocked", IsTransient: false));

        // Assert
        Assert.Equal(500, status);
    }

    [Theory]
    [MemberData(nameof(StatusRows))]
    public void ErrorOf_StatusAndBodyOfATypeOfErrors_GivesAnErrorEqualToTheOriginal(string name, int status)
    {
        // Arrange
        var original = new Error(ErrorTypesOfErrors.Named(name), "The detail.");

        // Act
        var error = ErrorProblems.ErrorOf(ErrorProblems.StatusCodeOf(original.Type), ErrorTypeBody.From(original.Type), original.Message);

        // Assert
        Assert.Equal((name, status, original), (name, ErrorProblems.StatusCodeOf(original.Type), error));
    }

    [Fact]
    public void ErrorOf_StatusAndBodyOfATypeOfTheConsumer_GivesAnErrorEqualToTheOriginal()
    {
        // Arrange
        const string name = "OrderLocked";
        var original = new Error(new ErrorType(600, name, IsTransient: true), "The order is locked.");

        // Act
        var error = ErrorProblems.ErrorOf(ErrorProblems.StatusCodeOf(original.Type), ErrorTypeBody.From(original.Type), original.Message);

        // Assert
        Assert.Equal((original, name, true), (error, error.Type.Name, error.Type.IsTransient));
    }

    [Theory]
    [MemberData(nameof(NoErrorTypeRows))]
    public void ErrorOf_BodyWithNoErrorType_GivesTheTypeOfTheStatus(int status, string expected)
    {
        // Arrange
        const string detail = "The detail of the host.";

        // Act
        var error = ErrorProblems.ErrorOf(status, errorType: null, detail);

        // Assert
        Assert.Equal((status, expected, detail), (status, error.Type.Name, error.Message));
    }
}
