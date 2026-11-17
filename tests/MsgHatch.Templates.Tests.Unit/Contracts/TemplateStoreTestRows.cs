namespace MsgHatch.Templates.Tests.Unit.Contracts;

/// <summary>The rows of the data-driven tests of <see cref="TemplateStoreTestsBase{TStore}"/>. A generic class has no static members (CA1000).</summary>
public static class TemplateStoreTestRows
{
    /// <summary>
    /// The scope folders of D199 and D201 that each store has. The folder that contains the root folder of a file store
    /// has a template of <c>TestEvent</c>. A scope with a dash differs between the stores, so each store tests it.
    /// </summary>
    public static TheoryData<string, string, string?> ScopeFolderRows => new()
    {
        { "letters", "acme", "Acme order {{OrderNumber}}" },
        { "digits", "42", "Company 42 order {{OrderNumber}}" },
        { "a leading underscore", "_42", null },
        { "the parent folder", "..", null },
        { "a path to another folder", "acme/../42", null },
    };
}
