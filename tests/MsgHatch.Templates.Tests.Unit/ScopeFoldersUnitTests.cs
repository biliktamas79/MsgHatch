using MsgHatch.Templates;

namespace MsgHatch.Templates.Tests.Unit;

/// <summary>The folder of a template scope in a file store, and its reverse (D201, D218).</summary>
public sealed class ScopeFoldersUnitTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-1")]
    [InlineData("ACME")]
    [InlineData("Acme")]
    [InlineData("42")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a b")]
    [InlineData("Ödön")]
    [InlineData("%41")]
    [InlineData("^a")]
    [InlineData("0b6c1f7e-2a44-4d8e-9a3b-61c0f5e2d7a9")]
    public void ScopeOf_FolderOfAScope_GivesThatScope(string scope)
    {
        // Act
        var result = ScopeFolders.ScopeOf(ScopeFolders.FolderOf(scope)!);

        // Assert
        Assert.Equal(scope, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Acme")]
    [InlineData("^A")]
    [InlineData("^")]
    [InlineData("^1")]
    [InlineData("a.b")]
    [InlineData("%41")]
    [InlineData("%ZZ")]
    [InlineData("%2e")]
    public void ScopeOf_NameThatFolderOfDoesNotMake_GivesNull(string folder)
    {
        // Act
        var result = ScopeFolders.ScopeOf(folder);

        // Assert
        Assert.Null(result);
    }
}
