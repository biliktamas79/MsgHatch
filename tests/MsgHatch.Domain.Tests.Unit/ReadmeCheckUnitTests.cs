using MsgHatch.Tests.Shared;

namespace MsgHatch.Domain.Tests.Unit;

public sealed class ReadmeCheckUnitTests
{
    private const string _readme = """
        # Package

        [//]: # (snippet: setup)
        ```csharp
        services.AddMsgHatch();
        ```

        [//]: # (snippet: options)
        ```json
        { "MsgHatch": { "Size": 3 } }
        ```

        ```shell
        dotnet add package MsgHatch.Domain
        ```

        [//]: # (options: SampleOptions)
        | Option | Type | Default | Rule |
        |---|---|---|---|
        | `Size` | `int` | `3` | 1 to 10 |
        | `Name` | `string` | none | Required |
        | `Tags` | `List<string>` | empty | none |
        """;

    private static readonly Dictionary<string, string> _regions = new(StringComparer.Ordinal) { ["setup"] = "services.AddMsgHatch();" };

    private static readonly Dictionary<string, ReadmeCheck.SnippetFile> _files = new(StringComparer.Ordinal)
    {
        ["options"] = new("json", """{ "MsgHatch": { "Size": 3 } }"""),
    };

    [Fact]
    public void FindSnippetFailures_BlocksEqualTheirSources_ReturnsNone()
    {
        // Act
        var failures = ReadmeCheck.FindSnippetFailures(_readme, _regions, _files);

        // Assert
        Assert.Empty(failures);
    }

    [Fact]
    public void FindSnippetFailures_ChangedLine_ReturnsTheSnippet()
    {
        // Arrange
        var regions = new Dictionary<string, string>(StringComparer.Ordinal) { ["setup"] = "services.AddMsgHatch(o => o.Roles = MsgHatchRoles.None);" };

        // Act
        var failures = ReadmeCheck.FindSnippetFailures(_readme, regions, _files);

        // Assert
        Assert.StartsWith("The snippet setup differs", Assert.Single(failures), StringComparison.Ordinal);
    }

    [Fact]
    public void FindSnippetFailures_MarkerWithNoRegion_ReturnsTheSnippet()
    {
        // Act
        var failures = ReadmeCheck.FindSnippetFailures(_readme, new Dictionary<string, string>(StringComparer.Ordinal), _files);

        // Assert
        Assert.Equal(["The snippet setup (csharp) has no source."], failures);
    }

    [Fact]
    public void FindSnippetFailures_RegionWithNoMarker_ReturnsTheRegion()
    {
        // Arrange
        var regions = new Dictionary<string, string>(_regions, StringComparer.Ordinal) { ["routing"] = "builder.AddMessage();" };

        // Act
        var failures = ReadmeCheck.FindSnippetFailures(_readme, regions, _files);

        // Assert
        Assert.Equal(["The region readme:routing has no snippet."], failures);
    }

    [Fact]
    public void FindSnippetFailures_FileWithNoMarker_ReturnsTheFile()
    {
        // Arrange
        var files = new Dictionary<string, ReadmeCheck.SnippetFile>(_files, StringComparer.Ordinal) { ["tenants"] = new("json", "{}") };

        // Act
        var failures = ReadmeCheck.FindSnippetFailures(_readme, _regions, files);

        // Assert
        Assert.Equal(["The file Readme/tenants.json has no snippet."], failures);
    }

    [Fact]
    public void FindSnippetFailures_CSharpBlockWithNoMarker_ReturnsTheBlock()
    {
        // Arrange
        var readme = _readme + "\n\n```csharp\nvar x = 1;\n```\n";

        // Act
        var failures = ReadmeCheck.FindSnippetFailures(readme, _regions, _files);

        // Assert
        Assert.Equal(["A csharp block has no snippet marker: var x = 1;"], failures);
    }

    [Fact]
    public void FindOptionsFailures_TableNamesEachOption_ReturnsNone()
    {
        // Act
        var failures = ReadmeCheck.FindOptionsFailures(_readme, [typeof(SampleOptions)]);

        // Assert
        Assert.Empty(failures);
    }

    [Fact]
    public void FindOptionsFailures_MissingOption_ReturnsTheOption()
    {
        // Arrange
        var readme = string.Join('\n', _readme.ReplaceLineEndings("\n").Split('\n').Where(line => !line.Contains("`Name`", StringComparison.Ordinal)));

        // Act
        var failures = ReadmeCheck.FindOptionsFailures(readme, [typeof(SampleOptions)]);

        // Assert
        Assert.Equal(["The table of SampleOptions has no row Name."], failures);
    }

    [Fact]
    public void FindOptionsFailures_UnknownOption_ReturnsTheRow()
    {
        // Arrange
        var readme = _readme + "\n| `Color` | `string` | none | none |";

        // Act
        var failures = ReadmeCheck.FindOptionsFailures(readme, [typeof(SampleOptions)]);

        // Assert
        Assert.Equal(["The table of SampleOptions names Color, which is no option."], failures);
    }

    [Fact]
    public void FindOptionsFailures_TypeWithNoTable_ReturnsTheType()
    {
        // Act
        var failures = ReadmeCheck.FindOptionsFailures(_readme, [typeof(SampleOptions), typeof(OtherOptions)]);

        // Assert
        Assert.Equal(["The README has no options table of OtherOptions."], failures);
    }

    [Fact]
    public void FindOptionsFailures_TableWithNoType_ReturnsTheTable()
    {
        // Act
        var failures = ReadmeCheck.FindOptionsFailures(_readme, []);

        // Assert
        Assert.Equal(["The options table of SampleOptions has no options type in the test."], failures);
    }

    private sealed class SampleOptions
    {
        public int Size { get; set; } = 3;

        public string Name { get; init; } = string.Empty;

        public List<string> Tags { get; } = [];

        public int Computed => Size * 2;
    }

    private sealed class OtherOptions
    {
        public bool Enabled { get; set; }
    }
}
