using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates;

namespace MsgHatch.Email.Liquid;

/// <summary>The engine factory of the Liquid engine (D216), for example <c>p.AsEmail("OrderShipped", LiquidTemplates.Engine)</c>.</summary>
public static class LiquidTemplates
{
    /// <summary>Gets the factory that makes the Liquid engine of a payload type for one tenant key.</summary>
    public static ITemplateEngineFactory Engine { get; } = new Factory();

    private sealed class Factory : ITemplateEngineFactory
    {
        public ITemplateEngine<TPayload> Create<TPayload>(IServiceProvider tenantServices, TenantKey key) =>
            new LiquidTemplateEngine<TPayload>(
                TemplateSelector.For(tenantServices, key),
                tenantServices.GetRequiredService<TimeProvider>());
    }
}
