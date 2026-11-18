using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MsgHatch.Templates.AdminUI.DependencyInjection;

namespace MsgHatch.Templates.Liquid.AdminUI.DependencyInjection;

/// <summary>The setup method of the Liquid editor (D242).</summary>
public static class LiquidTemplateEditorExtensions
{
    /// <summary>
    /// Registers <see cref="LiquidTemplateEditor"/> for the engine <c>liquid</c> (D240), and <see cref="TimeProvider.System"/>
    /// when the host has no time provider. The host loads the bundle of the scoped styles, <c>{assembly}.styles.css</c>.
    /// </summary>
    /// <param name="services">The services of the host.</param>
    /// <returns>The same services.</returns>
    public static IServiceCollection AddLiquidTemplateEditor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        return services.AddTemplateEditor<LiquidTemplateEditor>(LiquidTemplateEditor.EngineName);
    }
}
