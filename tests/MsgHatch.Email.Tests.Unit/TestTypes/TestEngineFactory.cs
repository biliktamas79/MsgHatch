using MsgHatch.Domain.Identity;
using MsgHatch.Templates;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>An engine factory of the tests: it makes the engine of a function, for the payload type of that engine.</summary>
/// <param name="create">The function that makes the engine of one tenant key.</param>
public sealed class TestEngineFactory(Func<IServiceProvider, TenantKey, object> create) : ITemplateEngineFactory
{
    public ITemplateEngine<TPayload> Create<TPayload>(IServiceProvider tenantServices, TenantKey key) =>
        (ITemplateEngine<TPayload>)create(tenantServices, key);
}
