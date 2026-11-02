using MsgHatch.Domain.Identity;

namespace MsgHatch.Email.Tests.Unit.Contracts;

/// <summary>The test data of <see cref="EmailTemplateEngineTestsBase{TEngine}"/>: what the engine interface cannot do.</summary>
public interface IEmailTemplateTestData
{
    /// <summary>
    /// Puts a template with no scope into the storage of the engine. Each text has its placeholders in the form
    /// <c>{Name}</c>, and the test data writes them in the syntax of its engine. A null text is a missing part.
    /// </summary>
    void Seed(MessageType type, string language, string subject, string html, string? text);
}
