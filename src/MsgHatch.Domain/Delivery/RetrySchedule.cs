using System.Buffers.Binary;

namespace MsgHatch.Domain.Delivery;

/// <summary>
/// The next try time of a failed try (section 4.14, D33): an exponential delay up to <see cref="RetryOptions.MaxDelay"/>,
/// with a jitter between 0.8 and 1.2 that the message id and the try number fix, so that a repeated calculation gives
/// the same time.
/// </summary>
internal static class RetrySchedule
{
    private const uint _knuthMultiplier = 2654435761;

    public static DateTime NextTryAtUtc(Guid messageId, int tryNumber, DateTime finishedAtUtc, RetryOptions retry)
    {
        var delayTicks = Math.Min(retry.MaxDelay.Ticks, retry.BaseDelay.Ticks * Math.Pow(2, tryNumber - 1));
        Span<byte> id = stackalloc byte[16];
        messageId.TryWriteBytes(id);
        var hash = BinaryPrimitives.ReadUInt32LittleEndian(id) ^ unchecked((uint)tryNumber * _knuthMultiplier);
        var factor = 0.8 + (0.4 * hash / uint.MaxValue);
        return finishedAtUtc + TimeSpan.FromTicks((long)(delayTicks * factor));
    }
}
