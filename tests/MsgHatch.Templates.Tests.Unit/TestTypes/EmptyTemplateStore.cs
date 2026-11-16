using MsgHatch.Domain.Failures;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Tests.Unit.TestTypes;

/// <summary>A shared template store with no template, for the tests of the generic setup method.</summary>
public sealed class EmptyTemplateStore : ITemplateStore
{
    public ValueTask<ErrorOr<TemplateSource?>> FindAsync(TemplateLookup lookup, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(null));
}
