using MsgHatch.Domain.Identity;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Tests.Unit.Contracts;

/// <summary>The test data of <see cref="TemplateEngineTestsBase{TEngine}"/>: what the engine interface cannot do.</summary>
public interface ITemplateTestData
{
    /// <summary>
    /// Puts the text of one part with no scope into the storage of the engine. The text has its placeholders in the
    /// form <c>{Name}</c>, and the test data writes them in the syntax of its engine.
    /// </summary>
    void Seed(MessageType type, string language, string part, TemplatePartKind kind, string text);

    /// <summary>Writes the placeholders <c>{Name}</c> of a text in the syntax of the engine.</summary>
    string InEngineSyntax(string text);
}
