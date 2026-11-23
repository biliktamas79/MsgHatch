using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SignalRDemo.CodePanel;
using SignalRDemo.Tests.Unit.TestTypes;

namespace SignalRDemo.Tests.Unit;

/// <summary>
/// The code panel of the dashboard (D316). Each block equals the region of its source file on the disk, with the indent
/// of the region marker removed, so the panel shows the code that runs.
/// </summary>
public sealed class CodeBlocksUnitTests
{
    [Fact]
    public async Task Blocks_EachStepOfThePlan_EqualsTheRegionOfItsSourceFile()
    {
        // Arrange
        await using var demo = DemoWebApp.Build();

        // Act
        var blocks = demo.App.Services.GetRequiredService<CodeBlocks>().Blocks;

        // Assert
        Assert.Equal(["connect", "join-tenant", "login", "connect-with-token", "hub", "user-id-provider", "router"], blocks.Select(static block => block.Region));
        Assert.All(blocks, static block => Assert.Equal(RegionOnTheDisk(block.File, block.Region), block.Code));
    }

    private static string RegionOnTheDisk(string file, string region)
    {
        var text = File.ReadAllText(Path.Combine(DemoProjectDirectory(), file)).ReplaceLineEndings("\n");
        var match = Regex.Match(
            text,
            $@"^(?<indent>[ ]*)(?://)?#region {Regex.Escape(region)}\n(?<code>.*?)\n[ ]*(?://)?#endregion",
            RegexOptions.Multiline | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));
        Assert.True(match.Success, $"{file} has no region {region}.");
        var indent = match.Groups["indent"].Value;
        return string.Join('\n', match.Groups["code"].Value.Split('\n').Select(line => line.StartsWith(indent, StringComparison.Ordinal) ? line[indent.Length..] : line));
    }

    private static string DemoProjectDirectory()
    {
        var testProject = typeof(CodeBlocksUnitTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static attribute => attribute.Key == "MsgHatch.TestProjectDirectory").Value!;
        return Path.Combine(testProject, "..", "..", "examples", "SignalRDemo");
    }
}
