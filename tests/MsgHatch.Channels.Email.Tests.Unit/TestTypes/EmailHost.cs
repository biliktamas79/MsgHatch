using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Channels.Email.DependencyInjection;
using NSubstitute;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Channels.Email.Tests.Unit.TestTypes;

/// <summary>Shared by the registration tests: the setup of a host with the in-memory stores, an email transport, and the test event.</summary>
public static class EmailHost
{
    public static MsgHatchBuilder AddMsgHatchWithStores(this IServiceCollection services, bool enableLogging = true) =>
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseInMemoryStores().UseTransport(static _ => AnEmailTransport());

    public static MsgHatchBuilder AddTestEvent(
        this MsgHatchBuilder builder, Action<MessageBuilder<TestEvent>> email, string name = "TestEvent") =>
        builder.AddMessage(name, 1, TestJsonContext.Default.TestEvent, m => email(m.WithRouter<TwoEmailRoutesRouter<TestEvent>>()));

    public static MsgHatchBuilder UseTestTemplates(this MsgHatchBuilder builder) =>
        builder.UseEmbeddedTemplates(typeof(EmbeddedTemplateFiles).Assembly, EmbeddedTemplateFiles.Prefix);

    public static ITransport AnEmailTransport()
    {
        var transport = Substitute.For<ITransport>();
        transport.Channel.Returns(EmailChannel.Id);
        return transport;
    }
}
