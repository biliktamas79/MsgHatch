namespace MsgHatch.Email.Tests.Unit.Contracts;

/// <summary>The rows of the data-driven tests of <see cref="TemplateStoreTestsBase{TStore}"/>. A generic class has no static members (CA1000).</summary>
public static class TemplateStoreTestRows
{
    /// <summary>
    /// The scope folders of D199 and D201. The folder <c>acme-1</c> exists, but its name is not a scope folder, and the
    /// folder that contains the root folder of a file store has a template of <c>TestEvent</c>.
    /// </summary>
    public static TheoryData<string, string, string?> ScopeFolderRows => new()
    {
        { "letters", "acme", "Acme order {{OrderNumber}}" },
        { "digits", "42", "Company 42 order {{OrderNumber}}" },
        { "a dash", "acme-1", null },
        { "a leading underscore", "_42", null },
        { "the parent folder", "..", null },
        { "a path to another folder", "acme/../42", null },
    };
}
