using MsgHatch.Domain.Identity;

namespace MsgHatch.Email.Engines;

/// <summary>
/// Makes the template engine of a payload type for one tenant key (D216). One factory object selects an engine for
/// each payload type, because C# cannot infer the type argument of a generic method group from its return type.
/// </summary>
public interface ITemplateEngineFactory
{
    /// <summary>Makes the engine of one payload type and tenant key.</summary>
    /// <typeparam name="TPayload">The type of the template payload.</typeparam>
    /// <param name="tenantServices">The service provider of the tenant services of the key.</param>
    /// <param name="key">The tenant key.</param>
    /// <returns>The engine.</returns>
    ITemplateEngine<TPayload> Create<TPayload>(IServiceProvider tenantServices, TenantKey key);
}
