using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.SignalR.DependencyInjection;
using NSubstitute;

namespace MsgHatch.SignalR.Tests.Unit.TestTypes;

/// <summary>Shared by the registration tests: a web host for SignalR, and the test event with a SignalR registration.</summary>
public static class SignalRHost
{
    /// <summary>
    /// The services of <see cref="TestHost.CreateServices"/>, and the <see cref="IHostApplicationLifetime"/> of a web
    /// host, which the services of <c>AddSignalR</c> need.
    /// </summary>
    public static IServiceCollection CreateServices(IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var services = TestHost.CreateServices(configuration: configuration);
        services.AddSingleton(Substitute.For<IHostApplicationLifetime>());
        return services;
    }

    public static MsgHatchBuilder AddTestEvent(this MsgHatchBuilder builder) =>
        builder.AddMessage("TestEvent", 1, TestJsonContext.Default.TestEvent, static m => m
            .WithRouter<SignalRRoutesRouter>()
            .WithPayload<TestPayload, TestPayloadLoader>(static p => p.AsSignalR("TestUpdate", 2, SignalRTestJsonContext.Default.SignalREnvelopeTestPayload)));
}
