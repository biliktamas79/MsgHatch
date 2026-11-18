using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Templates.Tests.Unit.TestTypes;
using NSubstitute;
using TestEvent = MsgHatch.Domain.Tests.Unit.TestTypes.TestEvent;
using TestJsonContext = MsgHatch.Domain.Tests.Unit.TestTypes.TestJsonContext;

namespace MsgHatch.Templates.Tests.Unit.Management;

/// <summary>The sample payload of the template registrations of a payload type and a message type (D254).</summary>
public sealed class WithTemplateSampleUnitTests(TemplateFolder folder) : IClassFixture<TemplateFolder>
{
    [Fact]
    public async Task Validate_TwoSamplesOfOnePayloadTypeAndOneMessageType_GivesTheSetupError()
    {
        // Arrange
        await using var provider = HostWith(b => b.AddOrderEmailEngine(
            PlaceholderTemplates.Engine<OrderEmail>, "Orders", samples: [TemplateEngineRegistrations.ASample(), TemplateEngineRegistrations.ASample("Other")]));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Contains(
            "WithTemplateSample gives the payload type OrderEmail of the message type Orders two samples. Give each payload type of a message type one sample.",
            FailuresOf(exception));
    }

    [Fact]
    public async Task Validate_OneSampleOfOnePayloadTypeForEachOfTwoMessageTypes_GivesNoSetupError()
    {
        // Arrange
        await using var provider = HostWith(b => b
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, "Orders", samples: [TemplateEngineRegistrations.ASample()])
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, "Returns", samples: [TemplateEngineRegistrations.ASample()]));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.DoesNotContain(FailuresOf(exception), static failure => failure.StartsWith(nameof(TemplateExtensions.WithTemplateSample), StringComparison.Ordinal));
    }

    [Fact]
    public void WithTemplateSample_NullSample_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() => builder.AddMessage("Orders", 1, TestJsonContext.Default.TestEvent, m => m.WithPayload<OrderEmail>(
            static (_, _) => Substitute.For<IPayloadLoader<TestEvent, OrderEmail>>(),
            static p => p.WithTemplateSample(null!))));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>The failures of each options type of the startup validation, also of other parts of the setup.</summary>
    private static IEnumerable<string> FailuresOf(Exception? exception) => exception switch
    {
        OptionsValidationException options => options.Failures,
        AggregateException all => all.InnerExceptions.SelectMany(FailuresOf),
        _ => [],
    };

    private ServiceProvider HostWith(Action<MsgHatchBuilder> configure)
    {
        var services = TestHost.CreateServices();
        configure(services.AddMsgHatch(static o => o.DefaultLanguage = "en").UseFileTemplates(folder.Root));
        return services.Build();
    }
}
