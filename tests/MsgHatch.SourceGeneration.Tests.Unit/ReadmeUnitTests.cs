using MsgHatch.Tests.Shared;

namespace MsgHatch.SourceGeneration.Tests.Unit;

/// <summary>The README check compares the README of the project with its test sources (D311). It has no code block and no options.</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();
}
