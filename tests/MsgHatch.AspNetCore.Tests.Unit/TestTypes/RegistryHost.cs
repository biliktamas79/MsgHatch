using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.AspNetCore.Tests.Unit.TestTypes;

/// <summary>
/// A web application on a <see cref="TestServer"/> with the registry endpoints and the in-memory reader of the test
/// channel, the second test channel, and the message type <c>TestEvent</c> on both channels. With a writer, the
/// writer is a substitute that gives no entries, saves each entry with the next revision, and resets each entry.
/// </summary>
public sealed class RegistryHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private RegistryHost(WebApplication app, IRegistryWriter writer)
    {
        _app = app;
        Writer = writer;
        Client = app.GetTestClient();
    }

    /// <summary>The writer substitute. A host with no writer does not register it.</summary>
    public IRegistryWriter Writer { get; }

    public HttpClient Client { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<RegistryHost> StartAsync(bool withWriter = true)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .AddTestChannel()
            .AddSecondTestChannel()
            .UseRouter(TestOpenRouter.Factory)
            .UseInMemoryStores()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>())
                .AsChannel(TestChannel.SecondId, "OtherEnvelope", 1, TestJsonContext.Default.OtherTestEnvelope, AnyComposer<OtherTestEnvelope>()));
        var writer = AWriter();
        _ = withWriter ? builder.Services.AddSingleton(writer) : builder.Services;
        var app = builder.Build();
        app.MapMsgHatchRegistry();
        await app.StartAsync(Ct);
        return new RegistryHost(app, writer);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync(Ct);
        await _app.DisposeAsync();
    }

    private static IRegistryWriter AWriter()
    {
        var writer = Substitute.For<IRegistryWriter>();
        writer.GetEntriesAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<RegistryEntries>.ForValue(new RegistryEntries([], []))));
        writer.SaveChannelAsync(default!, default).ReturnsForAnyArgs(static call => ValueTask.FromResult(ErrorOr<ChannelEntry>.ForValue(
            call.ArgAt<ChannelEntry>(0) with { Revision = call.ArgAt<ChannelEntry>(0).Revision + 1, LastModifiedAtUtc = TestHost.Start.UtcDateTime })));
        writer.SaveMessageTypeAsync(default!, default).ReturnsForAnyArgs(static call => ValueTask.FromResult(ErrorOr<MessageTypeEntry>.ForValue(
            call.ArgAt<MessageTypeEntry>(0) with { Revision = call.ArgAt<MessageTypeEntry>(0).Revision + 1, LastModifiedAtUtc = TestHost.Start.UtcDateTime })));
        writer.ResetChannelAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        writer.ResetMessageTypeAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        return writer;
    }

    private static Func<IServiceProvider, TenantKey, IEnvelopeComposer<TestEvent, TEnvelope>> AnyComposer<TEnvelope>() =>
        static (_, _) => Substitute.For<IEnvelopeComposer<TestEvent, TEnvelope>>();
}
