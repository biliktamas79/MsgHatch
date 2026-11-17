using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.AspNetCore;

/// <summary>The HTTP endpoints of the template administration (section 4.22, D217, D224).</summary>
public static class TemplateEndpoints
{
    /// <summary>
    /// Maps the minimal API endpoints of <see cref="ITemplateManager"/>: the list of the templates, and the read, the save,
    /// and the delete of one part at <c>{prefix}/{messageType}/{language}/{part}/{kind}</c>. Each route takes the optional
    /// query parameters <c>companyId</c> and <c>tenantId</c>, and uses the manager of <see cref="TenantKey.For"/> that
    /// tenant, or of <see cref="TenantKey.None"/>. The library adds no authorization, so the consumer adds it on the
    /// returned group, for example with <c>.RequireAuthorization("TemplateAdmin")</c>.
    /// </summary>
    /// <param name="routes">The endpoint route builder, for example the web application.</param>
    /// <param name="prefix">The path of the route group.</param>
    /// <returns>The route group.</returns>
    public static RouteGroupBuilder MapMsgHatchTemplates(this IEndpointRouteBuilder routes, string prefix = "/templates")
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(prefix);
        var group = routes.MapGroup(prefix);
        TemplateRoutes.MapTo(group);
        return group;
    }
}
