using ExampleApp.Channels;
using ExampleApp.Notices;
using ExampleApp.Tests.Unit.TestTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Dispatch.NamedPipes.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Email.DependencyInjection;
using static ExampleApp.Tests.Unit.TestTypes.ExampleHost;

namespace ExampleApp.Tests.Unit;

/// <summary>
/// The worker in the layout with separate processes (D101, D105): two hosts on one database file, each with its own DI
/// container and its own dispatch signal, an event consumer with no transport, and an email publisher. The publisher
/// finds the messages of the consumer through a poll, or through the named pipe signal (D188).
/// </summary>
public sealed class MsgHatchWorkerUnitTests : IDisposable
{
    /// <summary>The event id of a claim of the outbox dispatch store.</summary>
    private const int _outboxClaimId = 1304;

    private readonly Dictionary<string, string?> _database = TestDatabases.NewSection();
    private readonly WaitRecordingTimeProvider _time = new();

    public void Dispose() => _database.Delete();

    [Fact]
    public async Task ExecuteAsync_ConsumerAndEmailPublisherWithTheNamedPipeSignal_PublisherSendsTheEventOfTheConsumerWithNoPoll()
    {
        // Arrange
        var prefix = $"mh{Guid.NewGuid():N}"[..14];
        await using var consumer = Build(MsgHatchRoles.Consumer, builder => builder.UseNamedPipeDispatchSignal(o => o.PipeNamePrefix = prefix));
        await using var publisher = Build(
            MsgHatchRoles.Publisher, builder => builder.UseEmailClient<LogEmailClient>().UseNamedPipeDispatchSignal(o => o.PipeNamePrefix = prefix));
        consumer.GetRequiredService<IStartupValidator>().Validate();
        publisher.GetRequiredService<IStartupValidator>().Validate();
        await consumer.SeedScenarioAsync(TenantKey.None);
        await using var worker = await RunningWorker.StartAsync(publisher, Ct);
        await publisher.WaitForLogsAsync(_outboxClaimId, count: 1);
        await WaitUntilServedAsync($"{prefix}.outbox.Email");
        await consumer.AcceptAsync(new EventA([1], null, "a"));

        // Act
        await consumer.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        var emails = await publisher.WaitForLogsAsync(ExampleLoggerExtensions.EmailSentId, count: 1);
        Assert.Equal(["client1@acme.example | Notification A: a"], emails.Select(static email => $"{email.Field("To")} | {email.Field("Subject")}"));
    }

    [Fact]
    public async Task ExecuteAsync_ConsumerAndEmailPublisherOnOneFile_PublisherSendsTheEventOfTheConsumerAfterAPoll()
    {
        // Arrange
        await using var consumer = Build(MsgHatchRoles.Consumer);
        await using var publisher = Build(MsgHatchRoles.Publisher, static builder => builder.UseEmailClient<LogEmailClient>());
        consumer.GetRequiredService<IStartupValidator>().Validate();
        publisher.GetRequiredService<IStartupValidator>().Validate();
        await consumer.SeedScenarioAsync(TenantKey.None);
        await using var worker = await RunningWorker.StartAsync(publisher, Ct);
        var poll = await FirstPollOfTheLoopAsync();
        await consumer.AcceptAsync(new EventA([1], null, "a"));
        await consumer.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Act
        _time.Advance(poll);

        // Assert
        var emails = await publisher.WaitForLogsAsync(ExampleLoggerExtensions.EmailSentId, count: 1);
        Assert.Equal(["client1@acme.example | Notification A: a"], emails.Select(static email => $"{email.Field("To")} | {email.Field("Subject")}"));
    }

    /// <summary>
    /// Waits until the loop of the publisher waits for its first poll, and gives the poll. Before the worker starts, the
    /// stored registry of the consumer makes its refresh timer on the same clock, so the first wait of the clock is not the poll.
    /// </summary>
    private async Task<TimeSpan> FirstPollOfTheLoopAsync()
    {
        var poll = new DispatchOptions().PollInterval;
        TimeSpan wait;
        do
        {
            wait = await _time.NextWaitAsync();
        }
        while (wait != poll);

        return poll;
    }

    /// <summary>
    /// Waits until a process serves the pipe, because a notification that finds no server is lost (D192). The check lists
    /// the pipes, and does not connect to the pipe, so it takes no instance of the server.
    /// </summary>
    private static async Task WaitUntilServedAsync(string pipeName)
    {
        var start = TimeProvider.System.GetTimestamp();
        while (!IsServed(pipeName) && TimeProvider.System.GetElapsedTime(start) < TestHost.HangGuard)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), TimeProvider.System, Ct);
        }
    }

    private static bool IsServed(string pipeName) =>
        OperatingSystem.IsWindows()
            ? Directory.EnumerateFiles(@"\\.\pipe\").Contains(@"\\.\pipe\" + pipeName, StringComparer.OrdinalIgnoreCase)
            : File.Exists(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipeName));

    private ServiceProvider Build(MsgHatchRoles roles, Action<MsgHatchBuilder>? transports = null)
    {
        var services = CreateServices(_database, _time);
        var builder = services.AddExampleMsgHatch(o => o.Roles = roles);
        transports?.Invoke(builder);
        return services.Build();
    }
}
