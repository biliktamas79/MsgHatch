using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Unit;

/// <summary>The options tests of <see cref="EntityFrameworkStoreOptions"/>. They need no database.</summary>
public sealed class EntityFrameworkStoreOptionsValidatorUnitTests
{
    private const string _schema = nameof(EntityFrameworkStoreOptions.Schema);

    private const string _textCollation = nameof(EntityFrameworkStoreOptions.TextCollation);

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

    public static TheoryData<string?, string[]> TextCollationRows => new()
    {
        { null, [] },
        { "Latin1_General_100_BIN2", [] },
        { "C", [] },
        { "en-US-x-icu", [] },
        { "en_US.utf8", [] },
        { "", [_textCollation] },
        { "BIN2; DROP TABLE x", [_textCollation] },
        { "\"C\"", [_textCollation] },
    };

    [Fact]
    public void Validate_DefaultOptions_PassesWithTheSchemaMsghatch()
    {
        // Arrange
        var options = new EntityFrameworkStoreOptions();

        // Act
        var result = new EntityFrameworkStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(("msghatch", (string?)null, Array.Empty<string>()), (options.Schema, options.TextCollation, OptionsFailures.FailedProperties(result)));
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

    [Theory]
    [MemberData(nameof(TextCollationRows))]
    public void Validate_TextCollation_FailsWhenItHasACharacterOutsideTheNameOfACollation(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new EntityFrameworkStoreOptions { TextCollation = value };

        // Act
        var result = new EntityFrameworkStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
