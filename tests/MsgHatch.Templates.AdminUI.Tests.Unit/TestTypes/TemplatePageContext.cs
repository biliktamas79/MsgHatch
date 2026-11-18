using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.AdminUI.DependencyInjection;
using MsgHatch.Templates.Management;
using NSubstitute;

namespace MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes;

public abstract class TemplatePageContext : BunitContext
{
    protected TemplatePageContext()
    {
        Services.AddKeyedSingleton(TenantKey.None, Queries);
        Services.AddKeyedSingleton(TenantKey.None, Commands);
        Services.AddMsgHatchTemplatesAdminUI();
        Services.AddSingleton(EditorSettings);
    }

    protected TestEditorSettings EditorSettings { get; } = new();

    protected ITemplateQueries Queries { get; } = Substitute.For<ITemplateQueries>();

    protected ITemplateCommands Commands { get; } = Substitute.For<ITemplateCommands>();

    protected NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();
}
