using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Templates.Management;
using MsgHatch.Templates.Tests.Unit.Contracts;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Api.Client.Tests.Unit;

/// <summary>The use cases of <see cref="ITemplateQueries"/> on the HTTP client of <c>UseTemplateApiClient</c>, against the template endpoints.</summary>
/// <param name="folder">The temporary template folder of the test class.</param>
public sealed class HttpTemplateQueriesUnitTests(TemplateFolder folder) : TemplateQueriesTestsBase<ITemplateQueries>(folder)
{
    protected override ValueTask<IManagementTestHost<ITemplateQueries>> StartAsync(Action<IServiceCollection> addServices) =>
        HttpManagementHost.StartAsync<ITemplateQueries>(addServices);
}
