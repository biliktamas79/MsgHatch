using System.Buffers;
using MsgHatch.Email.Placeholders;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>A payload with a hand-written writer, which writes through <see cref="IBufferWriter{T}.GetMemory"/>.</summary>
public sealed record MemoryWriterPayload(string Name) : IPlaceholderPayload<MemoryWriterPayload>
{
    public static IPlaceholderWriter<MemoryWriterPayload> Writer { get; } = new MemoryWriter();

    private sealed class MemoryWriter : IPlaceholderWriter<MemoryWriterPayload>
    {
        public int IndexOf(ReadOnlySpan<char> propertyName) => propertyName.SequenceEqual(nameof(Name)) ? 0 : -1;

        public bool TryWrite(in MemoryWriterPayload payload, int propertyIndex, IBufferWriter<char> output, IFormatProvider culture)
        {
            if (propertyIndex != 0)
            {
                return false;
            }

            payload.Name.CopyTo(output.GetMemory(payload.Name.Length).Span);
            output.Advance(payload.Name.Length);
            return true;
        }
    }
}
