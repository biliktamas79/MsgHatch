using System.Text.Json;
using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Api.Contracts.Tests.Unit;

/// <summary>The wire form of an error type (D233) and its identity by the code (D238).</summary>
public sealed class ErrorTypeBodyUnitTests
{
    public static TheoryData<string> ErrorsRows => [.. ErrorTypesOfErrors.All.Select(static type => type.Name)];

    [Theory]
    [MemberData(nameof(ErrorsRows))]
    public void ToErrorType_BodyOfATypeOfErrors_GivesTheSameInstance(string name)
    {
        // Arrange
        var type = ErrorTypesOfErrors.Named(name);

        // Act
        var roundTrip = ErrorTypeBody.From(type).ToErrorType();

        // Assert
        Assert.Same(type, roundTrip);
    }

    [Fact]
    public void ToErrorType_KnownCodeWithAnotherName_GivesTheTypeOfErrorsWithItsLocalName()
    {
        // Arrange
        var body = new ErrorTypeBody(404, "NotFoundOfAnotherVersion", IsTransient: true);

        // Act
        var type = body.ToErrorType();

        // Assert
        Assert.Same(Errors.NotFound, type);
    }

    [Fact]
    public void ToErrorType_TypeOfTheConsumer_GivesAnEqualTypeWithTheNameAndTheFlagOfTheBody()
    {
        // Arrange
        var consumerType = new ErrorType(600, "OrderLocked", IsTransient: true);

        // Act
        var type = ErrorTypeBody.From(consumerType).ToErrorType();

        // Assert
        Assert.Equal((consumerType, 600, "OrderLocked", true), (type, type.Code, type.Name, type.IsTransient));
    }

    [Fact]
    public void From_NullType_ThrowsArgumentNullException()
    {
        // Act
        var exception = Record.Exception(static () => ErrorTypeBody.From(null!));

        // Assert
        Assert.Equal("type", Assert.IsType<ArgumentNullException>(exception).ParamName);
    }

    [Fact]
    public void ApiErrorJson_Body_WritesCamelCaseNamesAndReadsThemBack()
    {
        // Arrange
        var body = ErrorTypeBody.From(Errors.Unavailable);

        // Act
        var json = JsonSerializer.Serialize(body, ApiErrorJson.Default.ErrorTypeBody);
        var read = JsonSerializer.Deserialize(json, ApiErrorJson.Default.ErrorTypeBody);

        // Assert
        Assert.Equal(("""{"code":503,"name":"Unavailable","isTransient":true}""", body), (json, read));
    }

    [Theory]
    [InlineData("""{"code":503,"name":"Unavailable"}""")]
    [InlineData("""{"code":503,"name":null,"isTransient":true}""")]
    public void ApiErrorJson_BodyWithAMissingOrNullValue_ThrowsJsonException(string json)
    {
        // Act
        var exception = Record.Exception(() => JsonSerializer.Deserialize(json, ApiErrorJson.Default.ErrorTypeBody));

        // Assert
        Assert.IsType<JsonException>(exception);
    }
}
