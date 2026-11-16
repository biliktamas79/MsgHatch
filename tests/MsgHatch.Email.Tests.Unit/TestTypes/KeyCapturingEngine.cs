using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Engines;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>An engine of another type, which keeps its tenant key, for the registration with another engine. It finds no template.</summary>
public sealed class KeyCapturingEngine(TenantKey key) : ITemplateEngine<OrderEmail>
{
    public TenantKey Key { get; } = key;

    public ValueTask<ErrorOr<ITemplate<OrderEmail>>> PrepareAsync(TemplateRequest request, CancellationToken ct) =>
        ValueTask.FromResult<ErrorOr<ITemplate<OrderEmail>>>(new Error(Errors.NotFound));
}
