using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Tests.Unit.Failures;

public sealed class ErrorsUnitTests
{
    [Fact]
    public void Fields_EachField_HasTheCodeNameAndTransientFlagOfTheDesign()
    {
        // Arrange
        ErrorType[] expected =
        [
            new(400, "ValidationFailed", false),
            new(403, "Rejected", false),
            new(404, "NotFound", false),
            new(409, "Conflict", false),
            new(410, "Expired", false),
            new(415, "SerializationFailed", false),
            new(422, "TemplateInvalid", false),
            new(426, "UnsupportedVersion", true),
            new(429, "RateLimited", true),
            new(460, "OperationCancelled", true),
            new(499, "Uninitialized", false),
            new(500, "Exception", true),
            new(503, "Unavailable", true),
        ];

        // Act
        ErrorType[] actual =
        [
            Errors.ValidationFailed,
            Errors.Rejected,
            Errors.NotFound,
            Errors.Conflict,
            Errors.Expired,
            Errors.SerializationFailed,
            Errors.TemplateInvalid,
            Errors.UnsupportedVersion,
            Errors.RateLimited,
            Errors.OperationCancelled,
            Errors.Uninitialized,
            Errors.Exception,
            Errors.Unavailable,
        ];

        // Assert
        Assert.Equal(expected.Select(Describe), actual.Select(Describe));
    }

    [Fact]
    public void Find_CodeOfEachField_GivesTheSameInstance()
    {
        // Arrange
        ErrorType[] fields = [.. typeof(Errors).GetFields().Select(static field => (ErrorType)field.GetValue(null)!)];

        // Act
        ErrorType?[] found = [.. fields.Select(static type => Errors.Find(type.Code))];

        // Assert
        Assert.Equal((13, true), (fields.Length, fields.Zip(found).All(static pair => ReferenceEquals(pair.First, pair.Second))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(401)]
    [InlineData(1000)]
    public void Find_UnknownCode_GivesNull(int code)
    {
        // Act
        var type = Errors.Find(code);

        // Assert
        Assert.Null(type);
    }

    private static (int Code, string Name, bool IsTransient) Describe(ErrorType type) => (type.Code, type.Name, type.IsTransient);
}
