using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Liquid;

/// <summary>The engine factory of the Liquid engine (D216), for example <c>p.AsEmail("OrderShipped", LiquidTemplates.Engine)</c>.</summary>
public static class LiquidTemplates
{
    /// <summary>Gets the factory that makes the Liquid engine of a payload type for one tenant key.</summary>
    public static ITemplateEngineFactory Engine { get; } = new Factory();

    private sealed class Factory : ITemplateEngineFactory
    {
        public ITemplateEngine<TPayload> Create<TPayload>(IServiceProvider tenantServices, TenantKey key)
        {
            var services = tenantServices.GetRequiredService<IServiceProvider>();
            var selector = new TemplateSelector(
                key == TenantKey.None ? null : services.GetKeyedService<ITemplateStore>(key),
                services.GetService<ITemplateStore>(),
                services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.DefaultLanguage);
            return new LiquidTemplateEngine<TPayload>(selector, services.GetRequiredService<TimeProvider>());
        }
    }
}
