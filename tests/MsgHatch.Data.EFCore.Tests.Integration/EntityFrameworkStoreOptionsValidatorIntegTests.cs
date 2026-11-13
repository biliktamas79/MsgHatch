using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Integration;

/// <summary>The options tests of <see cref="EntityFrameworkStoreOptions"/>. They need no database.</summary>
public sealed class EntityFrameworkStoreOptionsValidatorIntegTests
{
    private const string _schema = nameof(EntityFrameworkStoreOptions.Schema);

    public static TheoryData<string?, string[]> SchemaRows => new()
    {
        { null, [_schema] },
        { "", [_schema] },
        { "   ", [_schema] },
        { "msghatch", [] },
        { "_Msg_Hatch2", [] },
        { "1msghatch", [_schema] },
        { "msg-hatch", [_schema] },
        { "msg hatch", [_schema] },
        { "üzenetek", [_schema] },
        { "msghatch].[dbo", [_schema] },
    };

    [Fact]
    public void Validate_DefaultOptions_PassesWithTheSchemaMsghatch()
    {
        // Arrange
        var options = new EntityFrameworkStoreOptions();

        // Act
        var result = new EntityFrameworkStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(("msghatch", Array.Empty<string>()), (options.Schema, OptionsFailures.FailedProperties(result)));
    }

    [Theory]
    [MemberData(nameof(SchemaRows))]
    public void Validate_Schema_FailsWhenItIsMissingOrNotAnIdentifier(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new EntityFrameworkStoreOptions { Schema = value! };

        // Act
        var result = new EntityFrameworkStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
