using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Identity;

/// <summary>
/// Gives the id of an outgoing message: a UUID of version 5 (RFC 9562) from the message id, the tenant, the delivery
/// target, and the channel (D11, section 4.8). The same inputs always give the same id, so a retry of an event
/// writes the same ids, and a store ignores the second insert.
/// </summary>
public static class DeterministicId
{
    private const int _stackBufferLength = 512;

    private static ReadOnlySpan<byte> NamespaceId =>
        [0x77, 0xb4, 0x68, 0x74, 0xba, 0xe1, 0x4f, 0xa7, 0xa7, 0xd9, 0xba, 0xec, 0x99, 0x6f, 0x4f, 0xc6];

    /// <summary>Gives the id of the outgoing message of one route of one event.</summary>
    /// <param name="messageId">The id of the event.</param>
    /// <param name="tenant">The tenant of the event, or null for no tenant.</param>
    /// <param name="target">The delivery target of the route.</param>
    /// <param name="channel">The channel of the route.</param>
    /// <returns>The id.</returns>
    public static Guid Create(Guid messageId, TenantId? tenant, DeliveryTarget target, ChannelId channel)
    {
        var length = HashInputLength(tenant, target, channel);
        byte[]? rented = null;
        var buffer = length <= _stackBufferLength
            ? stackalloc byte[_stackBufferLength]
            : rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            WriteHashInput(buffer, messageId, tenant, target, channel);
            return ToVersion5Uuid(buffer[..length]);
        }
        finally
        {
            ReturnToPool(rented);
        }
    }

    private static int HashInputLength(TenantId? tenant, DeliveryTarget target, ChannelId channel) =>
        NamespaceId.Length + 16
        + (tenant is { } t ? 1 + Encoding.UTF8.GetByteCount(t.Value) + 1 : 0)
        + 1 + Encoding.UTF8.GetByteCount(target.Id) + 1
        + Encoding.UTF8.GetByteCount(channel.Name);

    private static void WriteHashInput(
        Span<byte> buffer, Guid messageId, TenantId? tenant, DeliveryTarget target, ChannelId channel)
    {
        NamespaceId.CopyTo(buffer);
        var position = NamespaceId.Length;
        messageId.TryWriteBytes(buffer[position..], bigEndian: true, out var written);
        position += written;
        if (tenant is { } t)
        {
            buffer[position++] = (byte)'t';
            position += Encoding.UTF8.GetBytes(t.Value, buffer[position..]);
            buffer[position++] = 0;
        }

        buffer[position++] = target.Kind == DeliveryTargetKind.User ? (byte)'u' : (byte)'c';
        position += Encoding.UTF8.GetBytes(target.Id, buffer[position..]);
        buffer[position++] = 0;
        Encoding.UTF8.GetBytes(channel.Name, buffer[position..]);
    }

    [SuppressMessage("Security", "CA5350", Justification = "RFC 9562 defines UUID version 5 on SHA-1. The hash is an id, not a security check.")]
    private static Guid ToVersion5Uuid(ReadOnlySpan<byte> hashInput)
    {
        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(hashInput, hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }

    private static void ReturnToPool(byte[]? rented)
    {
        if (rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
