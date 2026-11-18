using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Tests.Unit.Management;

/// <summary>The registration of a template engine with the parts that a channel reads (D220).</summary>
public sealed class AddTemplateEngineUnitTests
{
    [Theory]
    [InlineData("")]
    [InlineData("1subject")]
    [InlineData("sub ject")]
    [InlineData("sub-ject")]
    [InlineData("sub.ject")]
    [InlineData("_subject")]
    public void AddTemplateEngine_PartNameThatIsNotAName_GivesArgumentException(string part)
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() => builder.AddOrderEmailEngine(
            PlaceholderTemplates.Engine<OrderEmail>, parts: [new TemplatePart("subject", TemplatePartKind.PlainText), new TemplatePart(part, TemplatePartKind.Html)]));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void AddTemplateEngine_NoPart_GivesArgumentException()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() => builder.AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, parts: []));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void AddTemplateEngine_PartNamesOfLettersDigitsAndUnderscores_RegistersTheEngine()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() => builder.AddOrderEmailEngine(
            PlaceholderTemplates.Engine<OrderEmail>, parts: [new TemplatePart("Body_2", TemplatePartKind.Html), new TemplatePart("s", TemplatePartKind.PlainText)]));

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("shared store")]
    [InlineData("store of each tenant")]
    [InlineData("engine")]
    [InlineData("all three")]
    public void TemplateSetupMethods_AnySetup_RegisterTheQueriesAndTheCommandsOneTime(string setup)
    {
        // Arrange
        var services = TestHost.CreateServices();
        var builder = services.AddMsgHatch();

        // Act
        if (setup is "shared store" or "all three")
        {
            builder.UseFileTemplates(Path.Combine(Path.GetTempPath(), "msghatch-tests", "manager"));
        }

        if (setup is "store of each tenant" or "all three")
        {
            builder.UseTenantTemplateStore(static (_, _) => new FileSystemTemplateStore(Path.Combine(Path.GetTempPath(), "msghatch-tests", "manager")));
        }

        if (setup is "engine" or "all three")
        {
            builder.AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>);
        }

        // Assert
        Assert.Equal(
            (setup, 1, 1, 1, 1),
            (setup, CountOf<Templates.Management.ITemplateQueries>(services, keyed: false), CountOf<Templates.Management.ITemplateQueries>(services, keyed: true),
                CountOf<Templates.Management.ITemplateCommands>(services, keyed: false), CountOf<Templates.Management.ITemplateCommands>(services, keyed: true)));
    }

    [Fact]
    public void PayloadBuilder_MessageOfAName_GivesThatMessageTypeAsItsType()
    {
        // Arrange
        MessageType? type = null;

        // Act
        TestHost.CreateServices().AddMsgHatch().AddMessage(
            "OrderShipped", 1, Domain.Tests.Unit.TestTypes.TestJsonContext.Default.TestEvent,
            m => m.WithPayload<OrderEmail>(static (_, _) => null!, p => type = p.Type));

        // Assert
        Assert.Equal(new MessageType("OrderShipped"), type);
    }

    private static int CountOf<TService>(IServiceCollection services, bool keyed) =>
        services.Count(descriptor => descriptor.ServiceType == typeof(TService) && descriptor.IsKeyedService == keyed);
}
