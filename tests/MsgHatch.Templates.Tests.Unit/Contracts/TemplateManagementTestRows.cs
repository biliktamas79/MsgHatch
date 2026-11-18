namespace MsgHatch.Templates.Tests.Unit.Contracts;

/// <summary>The rows of <see cref="TemplateQueriesTestsBase{T}"/> and <see cref="TemplateCommandsTestsBase{T}"/>.</summary>
public static class TemplateManagementTestRows
{
    public static TheoryData<string, string, string?, string> InvalidLookupRows => new()
    {
        { "an unknown language", "xx", null, "subject" },
        { "an empty language", "", null, "subject" },
        { "a language in capital letters", "HU", null, "subject" },
        { "a language that is not the name of its culture", "hu-hu", null, "subject" },
        { "a language with a private part", "en-US-x-test", null, "subject" },
        { "an empty company id", "en", "", "subject" },
        { "a company id with a control character", "en", "acme\u0001", "subject" },
        { "a company id whose folder name has 256 characters", "en", new string('A', 128), "subject" },
        { "an empty part", "en", null, "" },
        { "a part with a space", "en", null, "sub ject" },
        { "a part that starts with a digit", "en", null, "1subject" },
        { "a part with a dot", "en", null, "sub.ject" },
    };
}
