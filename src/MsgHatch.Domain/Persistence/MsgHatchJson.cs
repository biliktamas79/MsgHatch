using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Persistence;

/// <summary>
/// Writes a whole <see cref="ReceivedMessage"/> or <see cref="OutgoingMessage"/> as one JSON document, and reads it
/// back as its registered closed type (section 4.5). It writes camelCase names, enum values as strings (D23), and
/// null values. It writes a route trace with the discriminator property <c>$type</c>, and it does not write
/// <see cref="OutgoingMessage.Content"/>, because the property <c>envelope</c> holds it.
/// </summary>
public sealed class MsgHatchJson
{
    private const int _currentSchemaVersion = 1;
    private const string _traceContentTypePrefix = "Trace:";

    private static readonly ConcurrentDictionary<Type, ReceivedShape> _receivedShapes = new();
    private static readonly ConcurrentDictionary<Type, Func<OutgoingHeader, object, OutgoingMessage>> _outgoingFactories = new();

    private readonly IContentTypeRegistry _registry;
    private readonly FrozenDictionary<string, int> _newestVersions;
    private readonly FrozenDictionary<Type, ContentTypeEntry> _traceEntries;

    /// <summary>Makes the serializer for the entries of a content type registry.</summary>
    /// <param name="registry">The content type registry.</param>
    public MsgHatchJson(IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _newestVersions = registry.Entries
            .GroupBy(static entry => entry.ContentType, StringComparer.Ordinal)
            .ToFrozenDictionary(static group => group.Key, static group => group.Max(static entry => entry.Version), StringComparer.Ordinal);
        _traceEntries = registry.Entries
            .Where(static entry => entry.ContentType.StartsWith(_traceContentTypePrefix, StringComparison.Ordinal))
            .GroupBy(static entry => entry.ClrType)
            .ToFrozenDictionary(static group => group.Key, static group => group.MaxBy(static entry => entry.Version)!);
    }

    /// <summary>Writes a received message.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The UTF-8 JSON, or <see cref="Errors.SerializationFailed"/> for an unregistered type.</returns>
    public ErrorOr<byte[]> Serialize(ReceivedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var entry = FindFor(message.Type.Name, message.ContentVersion, message.GetType());
        return entry.IsError
            ? entry.Error
            : Write((message, entry.Value), static (writer, state) => WriteReceived(writer, state.message, state.Value));
    }

    /// <summary>Writes an outgoing message with its route trace.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The UTF-8 JSON, or <see cref="Errors.SerializationFailed"/> for an unregistered type.</returns>
    public ErrorOr<byte[]> Serialize(OutgoingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var entry = FindFor(message.ContentType, message.ContentVersion, message.GetType());
        if (entry.IsError)
        {
            return entry.Error;
        }

        var traceEntry = TraceEntryOf(message.Trace);
        return traceEntry.IsError
            ? traceEntry.Error
            : Write(
                (message, entry.Value, traceEntry.Value),
                static (writer, state) => WriteOutgoing(writer, state.message, state.Item2, state.Item3));
    }

    /// <summary>Reads a received message as its registered closed type.</summary>
    /// <param name="utf8Json">The UTF-8 JSON.</param>
    /// <returns>
    /// The message. A version that is newer than the code gives <see cref="Errors.UnsupportedVersion"/>, and an
    /// unknown type or invalid JSON gives <see cref="Errors.SerializationFailed"/>.
    /// </returns>
    public ErrorOr<ReceivedMessage> DeserializeReceived(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            using var document = JsonDocument.ParseValue(ref reader);
            return ReadReceived(document.RootElement);
        }
        catch (Exception exception) when (IsReadOrWriteFailure(exception))
        {
            return SerializationFailed(exception);
        }
    }

    /// <summary>Reads an outgoing message as its registered closed type. It ignores the properties that it does not know, for example <c>tries</c>.</summary>
    /// <param name="utf8Json">The UTF-8 JSON.</param>
    /// <returns>
    /// The message. A version that is newer than the code gives <see cref="Errors.UnsupportedVersion"/>, and an
    /// unknown type, an unknown trace discriminator, or invalid JSON gives <see cref="Errors.SerializationFailed"/>.
    /// </returns>
    public ErrorOr<OutgoingMessage> DeserializeOutgoing(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            using var document = JsonDocument.ParseValue(ref reader);
            return ReadOutgoing(document.RootElement);
        }
        catch (Exception exception) when (IsReadOrWriteFailure(exception))
        {
            return SerializationFailed(exception);
        }
    }

    private ErrorOr<ReceivedMessage> ReadReceived(JsonElement root)
    {
        var schemaVersion = ReadSchemaVersion(root);
        if (schemaVersion.IsError)
        {
            return schemaVersion.Error;
        }

        var type = new MessageType(root.GetProperty("type").GetString()!);
        var contentVersion = root.GetProperty("contentVersion").GetInt32();
        var entry = Find(type.Name, contentVersion);
        if (entry.IsError)
        {
            return entry.Error;
        }

        var header = new ReceivedHeader(
            schemaVersion.Value, ReadDelivery(root.GetProperty("delivery")), ReadTenant(root.GetProperty("tenant")), type,
            contentVersion, ReadFlags(root.GetProperty("flags")), root.GetProperty("traceParent").GetString());
        var content = ReadContent(root.GetProperty("event"), entry.Value);
        return ErrorOr<ReceivedMessage>.ForValue(ReceivedShapeOf(entry.Value.ClrType).Create(header, content));
    }

    private ErrorOr<OutgoingMessage> ReadOutgoing(JsonElement root)
    {
        var schemaVersion = ReadSchemaVersion(root);
        if (schemaVersion.IsError)
        {
            return schemaVersion.Error;
        }

        var contentType = root.GetProperty("contentType").GetString()!;
        var contentVersion = root.GetProperty("contentVersion").GetInt32();
        var entry = Find(contentType, contentVersion);
        if (entry.IsError)
        {
            return entry.Error;
        }

        var trace = ReadTrace(root.GetProperty("trace"));
        if (trace.IsError)
        {
            return trace.Error;
        }

        var header = new OutgoingHeader(
            schemaVersion.Value, ReadDelivery(root.GetProperty("delivery")), root.GetProperty("sourceMessageId").GetGuid(),
            ReadTenant(root.GetProperty("tenant")), new MessageType(root.GetProperty("type").GetString()!),
            new ChannelId(root.GetProperty("channel").GetString()!), ReadTarget(root.GetProperty("target")), contentType,
            contentVersion, ReadFlags(root.GetProperty("flags")), root.GetProperty("traceParent").GetString(), trace.Value);
        var content = ReadContent(root.GetProperty("envelope"), entry.Value);
        return ErrorOr<OutgoingMessage>.ForValue(OutgoingFactoryOf(entry.Value.ClrType)(header, content));
    }

    private ErrorOr<RouteTrace?> ReadTrace(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return ErrorOr<RouteTrace?>.ForValue(null);
        }

        var discriminator = element.GetProperty("$type").GetString() ?? string.Empty;
        var separator = discriminator.LastIndexOf('@');
        if (separator < 0
            || !int.TryParse(discriminator.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            return UnknownTraceDiscriminator(discriminator);
        }

        var entry = Find(discriminator[..separator], version);
        if (entry.IsError)
        {
            return entry.Error;
        }

        return element.Deserialize(entry.Value.ContentJsonTypeInfo) is RouteTrace trace
            ? trace
            : UnknownTraceDiscriminator(discriminator);
    }

    private ErrorOr<ContentTypeEntry> Find(string contentType, int version)
    {
        if (_registry.TryFind(contentType, version, out var entry))
        {
            return entry;
        }

        return _newestVersions.TryGetValue(contentType, out var newest) && version > newest
            ? new Error(Errors.UnsupportedVersion, $"The version {version} of {contentType} is newer than the code.")
            : new Error(Errors.SerializationFailed, $"The content type {contentType}@{version} is not registered.");
    }

    private ErrorOr<ContentTypeEntry> FindFor(string contentType, int version, Type messageType)
    {
        var entry = Find(contentType, version);
        return entry.IsSuccess && entry.Value.ClrType != messageType
            ? new Error(Errors.SerializationFailed, $"The content type {contentType}@{version} is registered for another type.")
            : entry;
    }

    private ErrorOr<ContentTypeEntry?> TraceEntryOf(RouteTrace? trace)
    {
        if (trace is null)
        {
            return ErrorOr<ContentTypeEntry?>.ForValue(null);
        }

        return _traceEntries.TryGetValue(trace.GetType(), out var entry)
            ? entry
            : new Error(Errors.SerializationFailed, $"The route trace type {trace.GetType().Name} is not registered.");
    }

    private static ErrorOr<int> ReadSchemaVersion(JsonElement root)
    {
        var version = root.GetProperty("schemaVersion").GetInt32();
        return version switch
        {
            > _currentSchemaVersion => new Error(Errors.UnsupportedVersion, $"The schema version {version} is newer than the code."),
            < 1 => new Error(Errors.SerializationFailed, $"The schema version {version} is not valid."),
            _ => version,
        };
    }

    private static ErrorOr<byte[]> Write<TState>(TState state, Action<Utf8JsonWriter, TState> write)
    {
        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                write(writer, state);
            }

            return buffer.WrittenSpan.ToArray();
        }
        catch (Exception exception) when (IsReadOrWriteFailure(exception))
        {
            return SerializationFailed(exception);
        }
    }

    private static void WriteReceived(Utf8JsonWriter writer, ReceivedMessage message, ContentTypeEntry entry)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", message.SchemaVersion);
        WriteDelivery(writer, message.Delivery);
        WriteTenant(writer, message.Tenant);
        writer.WriteString("type", message.Type.Name);
        writer.WriteNumber("contentVersion", message.ContentVersion);
        writer.WriteString("flags", message.Flags.ToString());
        writer.WriteString("traceParent", message.TraceParent);
        writer.WritePropertyName("event");
        JsonSerializer.Serialize(writer, ReceivedShapeOf(entry.ClrType).EventOf(message), entry.ContentJsonTypeInfo);
        writer.WriteEndObject();
    }

    private static void WriteOutgoing(
        Utf8JsonWriter writer, OutgoingMessage message, ContentTypeEntry entry, ContentTypeEntry? traceEntry)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", message.SchemaVersion);
        WriteDelivery(writer, message.Delivery);
        writer.WriteString("sourceMessageId", message.SourceMessageId);
        WriteTenant(writer, message.Tenant);
        writer.WriteString("type", message.Type.Name);
        writer.WriteString("channel", message.Channel.Name);
        writer.WriteStartObject("target");
        writer.WriteString("kind", message.Target.Kind.ToString());
        writer.WriteString("id", message.Target.Id);
        writer.WriteEndObject();
        writer.WriteString("contentType", message.ContentType);
        writer.WriteNumber("contentVersion", message.ContentVersion);
        writer.WriteString("flags", message.Flags.ToString());
        writer.WriteString("traceParent", message.TraceParent);
        writer.WritePropertyName("trace");
        WriteTrace(writer, message.Trace, traceEntry);
        writer.WritePropertyName("envelope");
        JsonSerializer.Serialize(writer, message.Content, entry.ContentJsonTypeInfo);
        writer.WriteEndObject();
    }

    private static void WriteTrace(Utf8JsonWriter writer, RouteTrace? trace, ContentTypeEntry? entry)
    {
        if (trace is null || entry is null)
        {
            writer.WriteNullValue();
            return;
        }

        var properties = JsonSerializer.SerializeToElement(trace, entry.ContentJsonTypeInfo);
        writer.WriteStartObject();
        writer.WriteString("$type", entry.Discriminator);
        foreach (var property in properties.EnumerateObject())
        {
            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteDelivery(Utf8JsonWriter writer, DeliveryState delivery)
    {
        writer.WriteStartObject("delivery");
        writer.WriteString("id", delivery.Id);
        writer.WriteString("status", delivery.Status.ToString());
        writer.WriteNumber("tryCount", delivery.TryCount);
        writer.WriteNumber("maxTryCount", delivery.MaxTryCount);
        writer.WriteString("createdAtUtc", delivery.CreatedAtUtc);
        WriteTime(writer, "lastTryAtUtc", delivery.LastTryAtUtc);
        writer.WriteString("nextTryAtUtc", delivery.NextTryAtUtc);
        WriteTime(writer, "leaseUntilUtc", delivery.LeaseUntilUtc);
        WriteTime(writer, "expiresAtUtc", delivery.ExpiresAtUtc);
        writer.WriteString("lastError", delivery.LastError);
        writer.WriteEndObject();
    }

    private static void WriteTime(Utf8JsonWriter writer, string name, DateTime? time)
    {
        if (time is { } value)
        {
            writer.WriteString(name, value);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void WriteTenant(Utf8JsonWriter writer, TenantId? tenant) =>
        writer.WriteString("tenant", tenant?.Value);

    private static DeliveryState ReadDelivery(JsonElement delivery) =>
        new(
            delivery.GetProperty("id").GetGuid(),
            Enum.Parse<DeliveryStatus>(delivery.GetProperty("status").GetString()!),
            delivery.GetProperty("tryCount").GetInt32(),
            delivery.GetProperty("maxTryCount").GetInt32(),
            delivery.GetProperty("createdAtUtc").GetDateTime(),
            ReadTime(delivery.GetProperty("lastTryAtUtc")),
            delivery.GetProperty("nextTryAtUtc").GetDateTime(),
            ReadTime(delivery.GetProperty("leaseUntilUtc")),
            ReadTime(delivery.GetProperty("expiresAtUtc")),
            delivery.GetProperty("lastError").GetString());

    private static DateTime? ReadTime(JsonElement time) =>
        time.ValueKind == JsonValueKind.Null ? null : time.GetDateTime();

    private static TenantId? ReadTenant(JsonElement tenant) =>
        tenant.ValueKind == JsonValueKind.Null ? null : new TenantId(tenant.GetString()!);

    private static DeliveryTarget ReadTarget(JsonElement target) =>
        new(Enum.Parse<DeliveryTargetKind>(target.GetProperty("kind").GetString()!), target.GetProperty("id").GetString()!);

    private static MessageFlags ReadFlags(JsonElement flags) => Enum.Parse<MessageFlags>(flags.GetString()!);

    private static object ReadContent(JsonElement content, ContentTypeEntry entry) =>
        content.Deserialize(entry.ContentJsonTypeInfo) ?? throw new JsonException($"The content of {entry.Discriminator} is null.");

    private static bool IsReadOrWriteFailure(Exception exception) =>
        exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException
            or ArgumentException or NotSupportedException or InvalidCastException;

    private static Error SerializationFailed(Exception exception) =>
        new(Errors.SerializationFailed, exception.Message, exception);

    private static Error UnknownTraceDiscriminator(string discriminator) =>
        new(Errors.SerializationFailed, $"The route trace discriminator '{discriminator}' is not registered.");

    private static ReceivedShape ReceivedShapeOf(Type clrType) =>
        _receivedShapes.GetOrAdd(clrType, static type =>
        {
            var shapes = typeof(ReceivedShapes<>).MakeGenericType(GenericArgumentOf(type, typeof(ReceivedMessage<>)));
            return new ReceivedShape(
                shapes.GetMethod(nameof(ReceivedShapes<object>.EventOf))!.CreateDelegate<Func<ReceivedMessage, object?>>(),
                shapes.GetMethod(nameof(ReceivedShapes<object>.Create))!.CreateDelegate<Func<ReceivedHeader, object, ReceivedMessage>>());
        });

    private static Func<OutgoingHeader, object, OutgoingMessage> OutgoingFactoryOf(Type clrType) =>
        _outgoingFactories.GetOrAdd(clrType, static type =>
            typeof(OutgoingShapes<>).MakeGenericType(GenericArgumentOf(type, typeof(OutgoingMessage<>)))
                .GetMethod(nameof(OutgoingShapes<object>.Create))!
                .CreateDelegate<Func<OutgoingHeader, object, OutgoingMessage>>());

    private static Type GenericArgumentOf(Type closedType, Type genericDefinition) =>
        closedType.IsGenericType && closedType.GetGenericTypeDefinition() == genericDefinition
            ? closedType.GetGenericArguments()[0]
            : throw new InvalidOperationException($"The registered type {closedType.Name} is not a {genericDefinition.Name}.");

    private readonly record struct ReceivedHeader(
        int SchemaVersion, DeliveryState Delivery, TenantId? Tenant, MessageType Type, int ContentVersion,
        MessageFlags Flags, string? TraceParent);

    private readonly record struct OutgoingHeader(
        int SchemaVersion, DeliveryState Delivery, Guid SourceMessageId, TenantId? Tenant, MessageType Type,
        ChannelId Channel, DeliveryTarget Target, string ContentType, int ContentVersion, MessageFlags Flags,
        string? TraceParent, RouteTrace? Trace);

    private sealed record ReceivedShape(
        Func<ReceivedMessage, object?> EventOf, Func<ReceivedHeader, object, ReceivedMessage> Create);

    private static class ReceivedShapes<TEvent>
    {
        public static object? EventOf(ReceivedMessage message) => ((ReceivedMessage<TEvent>)message).Event;

        public static ReceivedMessage<TEvent> Create(ReceivedHeader header, object content) =>
            new ReceivedMessage<TEvent>(
                header.SchemaVersion, header.Delivery, header.Tenant, header.Type, header.ContentVersion, header.Flags,
                header.TraceParent, (TEvent)content);
    }

    private static class OutgoingShapes<TEnvelope>
        where TEnvelope : class
    {
        public static OutgoingMessage<TEnvelope> Create(OutgoingHeader header, object content) =>
            new OutgoingMessage<TEnvelope>(
                header.SchemaVersion, header.Delivery, header.SourceMessageId, header.Tenant, header.Type, header.Channel,
                header.Target, header.ContentType, header.ContentVersion, header.Flags, header.TraceParent, header.Trace,
                (TEnvelope)content);
    }
}
