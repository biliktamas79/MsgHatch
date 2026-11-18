using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MsgHatch.Templates.AdminUI.DependencyInjection;

/// <summary>The setup methods of the template admin UI (D239, D240).</summary>
public static class TemplatesAdminUIExtensions
{
    /// <summary>
    /// Adds the template admin UI. The pages use <c>ITemplateQueries</c> and <c>ITemplateCommands</c> of
    /// <c>TenantKey.None</c>, so the host selects the HTTP client or the in-process implementation in its setup (D235). The
    /// host adds the authorization, and maps the pages with the assembly of <see cref="TemplateAdminRoutes"/> as an additional
    /// assembly of its router. The method keeps the pages in a trimmed host, and registers <see cref="TimeProvider.System"/>
    /// for the pause of the preview when the host has no time provider (D257).
    /// </summary>
    /// <param name="services">The services of the host.</param>
    /// <returns>The same services.</returns>
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TemplateListPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TemplateEditPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TemplateViewPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlainTemplateEditor))]
    public static IServiceCollection AddMsgHatchTemplatesAdminUI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TemplateEditors>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// Registers the editor of the template texts of one engine (D240). The edit page shows it for a part that only this
    /// engine reads. A later registration of the same engine replaces an earlier one.
    /// </summary>
    /// <typeparam name="TEditor">The editor component. The trimming keeps all its members (D237).</typeparam>
    /// <param name="services">The services of the host.</param>
    /// <param name="engine">The name of the engine, for example <c>liquid</c>.</param>
    /// <returns>The same services.</returns>
    public static IServiceCollection AddTemplateEditor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEditor>(
        this IServiceCollection services, string engine)
        where TEditor : TemplateEditorBase
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(engine);
        services.TryAddSingleton<TemplateEditors>();
        services.AddSingleton(new TemplateEditorRegistration(engine, typeof(TEditor)));
        return services;
    }
}
