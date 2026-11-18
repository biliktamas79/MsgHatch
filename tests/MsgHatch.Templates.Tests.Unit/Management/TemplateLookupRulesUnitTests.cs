using MsgHatch.Domain.Identity;
using MsgHatch.Templates;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.Tests.Unit.Management;

/// <summary>The rules of a template request that need no state (D234), with a row for each rule.</summary>
public sealed class TemplateLookupRulesUnitTests
{
    public static TheoryData<string, string?, string, int?> LookupRows => new()
    {
        { "a valid lookup with no scope", null, "subject", null },
        { "a valid company id", "acme-1", "subject", null },
        { "a company id whose folder name has 255 characters", new string('A', 127) + "a", "subject", null },
        { "an empty company id", "", "subject", 400 },
        { "a company id with a control character", "acme\u0001", "subject", 400 },
        { "a company id whose folder name has 256 characters", new string('A', 128), "subject", 400 },
        { "an empty part", null, "", 400 },
        { "a part with a space", null, "sub ject", 400 },
        { "a part that starts with a digit", null, "1subject", 400 },
        { "a part with a dot", null, "sub.ject", 400 },
    };

    [Theory]
    [MemberData(nameof(LookupRows))]
    public void Check_Lookup_GivesTheCodeOfTheRule(string row, string? scope, string part, int? expected)
    {
        // Arrange
        var lookup = new TemplateLookup(new MessageType("TestEvent"), "xx", scope, part, TemplatePartKind.PlainText);

        // Act
        var error = TemplateLookupRules.Check(lookup);

        // Assert
        Assert.Equal((row, expected), (row, error?.Type.Code));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("acme-1", null)]
    [InlineData("", 400)]
    [InlineData("acme\u0001", 400)]
    public void Check_Query_GivesTheCodeOfTheRuleOfTheScope(string? scope, int? expected)
    {
        // Act
        var error = TemplateLookupRules.Check(new TemplateQuery(Scope: scope));

        // Assert
        Assert.Equal(expected, error?.Type.Code);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1_000_000, null)]
    [InlineData(1_000_001, 400)]
    public void CheckText_Length_GivesTheCodeOfTheLimit(int length, int? expected)
    {
        // Act
        var error = TemplateLookupRules.CheckText(new string('x', length));

        // Assert
        Assert.Equal(expected, error?.Type.Code);
    }

    [Fact]
    public void CheckAndCheckText_NullArgument_ThrowArgumentNullException()
    {
        // Act
        var query = Record.Exception(static () => TemplateLookupRules.Check((TemplateQuery)null!));
        var text = Record.Exception(static () => TemplateLookupRules.CheckText(null!));

        // Assert
        Assert.Equal(("query", "text"), (Assert.IsType<ArgumentNullException>(query).ParamName, Assert.IsType<ArgumentNullException>(text).ParamName));
    }
}
