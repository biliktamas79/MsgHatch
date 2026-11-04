using System.Globalization;
using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Data.Sqlite;

/// <summary>
/// The stored text of the values (section 4.5): times as ISO 8601 text in UTC with 7 fractional digits and the suffix
/// <c>Z</c>, which sorts in time order (D57), ids in the format <c>D</c>, an empty text for no tenant (D114), and the
/// columns of the delivery state that the two message tables share.
/// </summary>
internal static class SqliteValues
{
    public const string DeliveryColumns =
        "Id, Status, TryCount, MaxTryCount, CreatedAtUtc, LastTryAtUtc, NextTryAtUtc, LeaseUntilUtc, ExpiresAtUtc, LastError";

    public const string DeliveryParameters =
        "@id, @status, @tryCount, @maxTryCount, @createdAtUtc, @lastTryAtUtc, @nextTryAtUtc, @leaseUntilUtc, @expiresAtUtc, @lastError";

    /// <summary>The delivery state of a row as the JSON object that <c>MsgHatchJson</c> reads.</summary>
    public const string DeliveryDocument = """
        json_object(
            'id', Id, 'status', Status, 'tryCount', TryCount, 'maxTryCount', MaxTryCount, 'createdAtUtc', CreatedAtUtc,
            'lastTryAtUtc', LastTryAtUtc, 'nextTryAtUtc', NextTryAtUtc, 'leaseUntilUtc', LeaseUntilUtc,
            'expiresAtUtc', ExpiresAtUtc, 'lastError', LastError)
        """;

    private const string _timeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public static string Time(DateTime time) => time.ToString(_timeFormat, CultureInfo.InvariantCulture);

    public static string? Time(DateTime? time) => time is { } value ? Time(value) : null;

    public static DateTime ParseTime(string text) =>
        DateTime.ParseExact(text, _timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static string Id(Guid id) => id.ToString("D");

    public static string Tenant(TenantId? tenant) => tenant?.Value ?? string.Empty;

    public static SqliteCommand Command(this SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    public static SqliteCommand With(this SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public static SqliteCommand WithDelivery(this SqliteCommand command, DeliveryState delivery) => command
        .With("@id", Id(delivery.Id))
        .With("@status", delivery.Status.ToString())
        .With("@tryCount", delivery.TryCount)
        .With("@maxTryCount", delivery.MaxTryCount)
        .With("@createdAtUtc", Time(delivery.CreatedAtUtc))
        .With("@lastTryAtUtc", Time(delivery.LastTryAtUtc))
        .With("@nextTryAtUtc", Time(delivery.NextTryAtUtc))
        .With("@leaseUntilUtc", Time(delivery.LeaseUntilUtc))
        .With("@expiresAtUtc", Time(delivery.ExpiresAtUtc))
        .With("@lastError", delivery.LastError);
}
