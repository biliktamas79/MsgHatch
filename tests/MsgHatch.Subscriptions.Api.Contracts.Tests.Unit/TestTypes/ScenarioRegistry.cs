using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Persistence;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Api.Contracts.Tests.Unit.TestTypes;

/// <summary>
/// The host of the subscription manager tests, for its content type registry with the settings types of the channels
/// Email, SignalR, and Broker, and the trace type <c>Trace:Subscription</c>. The channel <c>Sms</c> has no settings type.
/// </summary>
public sealed class ScenarioRegistry : IAsyncDisposable
{
    private readonly ServiceProvider _provider = ManagerHost.Build(TestHost.NewFakeTime());

    public IContentTypeRegistry Registry => _provider.GetRequiredService<IContentTypeRegistry>();

    public IServiceProvider Services => _provider;

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
