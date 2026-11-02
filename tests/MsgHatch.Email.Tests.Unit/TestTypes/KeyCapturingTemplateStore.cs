using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>A template store that keeps its tenant key, for the tests of the generic setup methods. It finds no template.</summary>
public sealed class KeyCapturingTemplateStore(TenantKey key) : ITemplateStore
{
    public TenantKey Key { get; } = key;

    public ValueTask<ErrorOr<TemplateSource?>> FindAsync(TemplateLookup lookup, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(null));
}
