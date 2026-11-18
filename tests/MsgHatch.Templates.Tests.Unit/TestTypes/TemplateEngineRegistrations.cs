using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Templates;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Placeholders;
using NSubstitute;

namespace MsgHatch.Templates.Tests.Unit.TestTypes;

/// <summary>
/// Registers the engine of <see cref="OrderEmail"/> for a message type of <see cref="TestEvent"/> with no channel, as a
/// channel package does with <c>AddTemplateEngine</c> (D220).
/// </summary>
public static class TemplateEngineRegistrations
{
    /// <summary>The parts of the email channel: the subject, the html body, and the text body.</summary>
    public static readonly TemplatePart[] EmailParts =
        [new("subject", TemplatePartKind.PlainText), new("body", TemplatePartKind.Html), new("body", TemplatePartKind.PlainText)];

    public static MsgHatchBuilder AddOrderEmailEngine(
        this MsgHatchBuilder builder, Func<IServiceProvider, TenantKey, ITemplateEngine<OrderEmail>> engine, string messageType = "TestEvent",
        IReadOnlyList<TemplatePart>? parts = null, string engineName = PlaceholderTemplates.EngineName) =>
        builder.AddMessage(messageType, 1, TestJsonContext.Default.TestEvent, m => m.WithPayload<OrderEmail>(
            static (_, _) => Substitute.For<IPayloadLoader<TestEvent, OrderEmail>>(),
            p => p.AddTemplateEngine($"Email:{messageType}", engineName, parts ?? EmailParts, engine)));
}
