using ExampleApp.Channels;
using ExampleApp.Notices;
using ExampleApp.Tests.Unit.TestTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Email.DependencyInjection;
using static ExampleApp.Tests.Unit.TestTypes.ExampleHost;

namespace ExampleApp.Tests.Unit;

/// <summary>
/// The worker in the layout with separate processes (D101, D105): two hosts on one database file, each with its own DI
/// container and its own dispatch signal, an event consumer with no transport, and an email publisher with a poll.
/// </summary>
public sealed class MsgHatchWorkerUnitTests : IDisposable
{
    private readonly Dictionary<string, string?> _database = TestDatabases.NewSection();
    private readonly WaitRecordingTimeProvider _time = new();

    public void Dispose() => _database.Delete();

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
        var poll = await _time.NextWaitAsync();
        await consumer.AcceptAsync(new EventA([1], null, "a"));
        await consumer.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Act
        _time.Advance(poll);

        // Assert
        var emails = await publisher.WaitForLogsAsync(ExampleLoggerExtensions.EmailSentId, count: 1);
        Assert.Equal(["client1@acme.example | Notification A: a"], emails.Select(static email => $"{email.Field("To")} | {email.Field("Subject")}"));
    }

    private ServiceProvider Build(MsgHatchRoles roles, Action<MsgHatchBuilder>? transports = null)
    {
        var services = CreateServices(_database, _time);
        var builder = services.AddExampleMsgHatch(o => o.Roles = roles);
        transports?.Invoke(builder);
        return services.Build();
    }
}
