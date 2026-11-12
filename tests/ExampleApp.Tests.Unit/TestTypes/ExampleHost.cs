using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace ExampleApp.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the tests of the example: a DI container with the configuration of one database file, a fake clock, and
/// fake logging, as a host of the example gives them before it calls <see cref="ExampleSetup.AddExampleMsgHatch"/>.
/// </summary>
public static class ExampleHost
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static IServiceCollection CreateServices(Dictionary<string, string?> database, TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(database).Build());
        services.AddSingleton(time ?? new WaitRecordingTimeProvider());
        services.AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Debug).AddFakeLogging());
        return services;
    }

    public static string ConnectionString(this Dictionary<string, string?> database) => database["MsgHatch:Sqlite:ConnectionString"]!;

    public static void Delete(this Dictionary<string, string?> database) => TestDatabases.Delete(database.ConnectionString());

    /// <summary>Waits until the log has <paramref name="count"/> records of one event id, and gives them.</summary>
    public static async Task<IReadOnlyList<FakeLogRecord>> WaitForLogsAsync(this IServiceProvider provider, int eventId, int count)
    {
        var collector = provider.GetFakeLogCollector();
        var timeout = TimeProvider.System.GetTimestamp();
        while (true)
        {
            List<FakeLogRecord> records = [.. collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
            if (records.Count >= count || TimeProvider.System.GetElapsedTime(timeout) > TestHost.HangGuard)
            {
                return records;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), TimeProvider.System, Ct);
        }
    }

    /// <summary>The value of one field of the structured state of a log record.</summary>
    public static string? Field(this FakeLogRecord record, string name) =>
        record.StructuredState!.Single(state => state.Key == name).Value;
}
