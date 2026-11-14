using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using WolverineExample.Consumer.Startup;

namespace WolverineExample.Consumer.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the test classes of the consumer: a consumer host with only the role <see cref="MsgHatchRoles.Consumer"/> on
/// a SQLite file in a temporary folder, with the seeded scenario, the fake clock, and the Wolverine handler.
/// </summary>
public sealed class ConsumerTestHost : IAsyncDisposable
{
    private readonly Dictionary<string, string?> _database = TestDatabases.NewSection();

    private ConsumerTestHost(FakeTimeProvider time)
    {
        Time = time;
        var services = TestHost.CreateServices(time, _database);
        services.AddConsumerMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer);
        Provider = services.Build();
        Receiver = ActivatorUtilities.CreateInstance<DomainEventReceiver>(Provider);
    }

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FakeTimeProvider Time { get; }

    public ServiceProvider Provider { get; }

    public DomainEventReceiver Receiver { get; }

    public static async Task<ConsumerTestHost> StartAsync()
    {
        var host = new ConsumerTestHost(TestHost.NewFakeTime());
        await ConsumerStartup.SeedAsync(host.Provider, Ct);
        return host;
    }

    /// <summary>Routes the events of the inbox, so the outbox stores their messages at the time of the clock.</summary>
    public async Task ProcessInboxAsync() => await Provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(100, Ct);

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        TestDatabases.Delete(_database["MsgHatch:Sqlite:ConnectionString"]!);
    }
}
