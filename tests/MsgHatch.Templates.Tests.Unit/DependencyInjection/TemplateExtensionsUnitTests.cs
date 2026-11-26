using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Tests.Unit.DependencyInjection;

/// <summary>The options of <c>UseFileTemplates</c> under the root of the configuration sections (D328, D329).</summary>
public sealed class TemplateExtensionsUnitTests
{
    [Fact]
    public async Task UseFileTemplates_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:FileTemplates:RootPath"] = "root-templates",
            ["MsgHatch:FileTemplates:RootPath"] = "default-templates",
        });
        services.AddMsgHatch(configSectionPath: "Messaging").UseFileTemplates();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<FileTemplateOptions>>().Value;

        // Assert
        Assert.Equal("root-templates", options.RootPath);
    }
}
