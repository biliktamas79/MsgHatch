using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.AdminUI;
using MsgHatch.Templates.AdminUI.DependencyInjection;
using MsgHatch.Templates.Liquid.AdminUI.DependencyInjection;
using MsgHatch.Templates.Management;
using NSubstitute;

namespace MsgHatch.Templates.Liquid.AdminUI.Tests.Unit;

public sealed class LiquidTemplateEditorExtensionsUnitTests : BunitContext
{
    [Fact]
    public void AddLiquidTemplateEditor_PartThatOnlyTheLiquidEngineReads_ShowsTheLiquidEditorOnTheEditPage()
    {
        // Arrange
        var type = new MessageType("Requests");
        var queries = Substitute.For<ITemplateQueries>();
        queries.GetTemplatePartsAsync(type, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(
            ErrorOr<IReadOnlyList<TemplatePartInfo>>.ForValue([new TemplatePartInfo(type, new TemplatePart("body", TemplatePartKind.Html), ["liquid"], HasSample: true)])));
        queries.GetTemplateAsync(default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ValueTask.FromResult(
            ErrorOr<TemplateSource>.ForValue(new TemplateSource(new TemplateKey("id", 3), "<p>{{ Name }}</p>"))));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddKeyedSingleton(TenantKey.None, queries);
        Services.AddKeyedSingleton(TenantKey.None, Substitute.For<ITemplateCommands>());
        Services.AddMsgHatchTemplatesAdminUI().AddLiquidTemplateEditor();

        // Act
        var cut = Render<TemplateEditPage>(p => p.Add(x => x.TypeName, "Requests").Add(x => x.Language, "en").Add(x => x.Part, "body").Add(x => x.KindName, "Html"));

        // Assert
        Assert.Single(cut.FindAll("[data-editor=liquid]"));
    }

    [Fact]
    public void ComponentTypes_OfTheLibrary_SetNoRenderMode()
    {
        // Act
        var withRenderMode = typeof(LiquidTemplateEditor).Assembly.GetTypes()
            .Where(static type => typeof(IComponent).IsAssignableFrom(type) && type.GetCustomAttribute<RenderModeAttribute>(inherit: true) is not null);

        // Assert
        Assert.Empty(withRenderMode);
    }
}
