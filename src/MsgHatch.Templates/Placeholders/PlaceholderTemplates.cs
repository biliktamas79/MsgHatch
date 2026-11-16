using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates.Placeholders;

/// <summary>The engine factory of the placeholder engine, for a channel that registers it with <c>AddTemplateEngine</c> (D212).</summary>
public static class PlaceholderTemplates
{
    /// <summary>Makes the placeholder engine of a payload type for one tenant key, with the selector of <see cref="TemplateSelector.For"/>.</summary>
    /// <typeparam name="TPayload">The payload type, with the placeholder writer of the source generator.</typeparam>
    /// <param name="tenantServices">The service provider of the tenant services of the key.</param>
    /// <param name="key">The tenant key.</param>
    /// <returns>The engine.</returns>
    public static ITemplateEngine<TPayload> Engine<TPayload>(IServiceProvider tenantServices, TenantKey key)
        where TPayload : IPlaceholderPayload<TPayload> =>
        new PlaceholderTemplateEngine<TPayload>(TemplateSelector.For(tenantServices, key));
}
