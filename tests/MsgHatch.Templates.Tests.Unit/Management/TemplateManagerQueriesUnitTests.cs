using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.Management;
using MsgHatch.Templates.Tests.Unit.Contracts;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Tests.Unit.Management;

/// <summary>The use cases of <see cref="ITemplateQueries"/> on the in-process template manager.</summary>
/// <param name="folder">The temporary template folder of the test class.</param>
public sealed class TemplateManagerQueriesUnitTests(TemplateFolder folder) : TemplateQueriesTestsBase<ITemplateQueries>(folder)
{
    protected override ValueTask<IManagementTestHost<ITemplateQueries>> StartAsync(Action<IServiceCollection> addServices) =>
        InProcessManagementHost.StartAsync<ITemplateQueries>(addServices);
}
