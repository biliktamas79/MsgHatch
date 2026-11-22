using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Broker.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Broker.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the registration tests: the test event with a broker registration, and the content type of
/// <see cref="BrokerMessages"/> for a host with no message type, which the broker transport needs (D299).
/// </summary>
public static class BrokerHost
{
    public static MsgHatchBuilder AddContentTypeOfBrokerMessages(this MsgHatchBuilder builder)
    {
        builder.Services.AddSingleton(BrokerMessages.ARegistry());
        return builder;
    }

    public static MsgHatchBuilder AddTestEvent(this MsgHatchBuilder builder) =>
        builder.AddMessage("TestEvent", 1, TestJsonContext.Default.TestEvent, static m => m
            .WithRouter<BrokerRoutesRouter>()
            .WithPayload<TestPayload, TestPayloadLoader>(static p => p.AsBroker("TestMessage", 2, BrokerTestJsonContext.Default.BrokerEnvelopeTestPayload)));
}
