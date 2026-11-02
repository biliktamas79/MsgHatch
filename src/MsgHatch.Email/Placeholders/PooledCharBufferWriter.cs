using System.Buffers;

namespace MsgHatch.Email.Placeholders;

/// <summary>A text buffer on <see cref="ArrayPool{T}.Shared"/> (section 4.10). Dispose returns the array to the pool.</summary>
internal sealed class PooledCharBufferWriter : IBufferWriter<char>, IDisposable
{
    private char[] _buffer = ArrayPool<char>.Shared.Rent(256);
    private int _written;

    public ReadOnlySpan<char> WrittenSpan => _buffer.AsSpan(0, _written);

    public void Clear() => _written = 0;

    public void Advance(int count) => _written += count;

    public Memory<char> GetMemory(int sizeHint = 0)
    {
        EnsureFree(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<char> GetSpan(int sizeHint = 0)
    {
        EnsureFree(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public void Dispose()
    {
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = [];
    }

    private void EnsureFree(int sizeHint)
    {
        var needed = Math.Max(sizeHint, 1);
        if (_buffer.Length - _written >= needed)
        {
            return;
        }

        var larger = ArrayPool<char>.Shared.Rent(Math.Max(_buffer.Length * 2, _written + needed));
        WrittenSpan.CopyTo(larger);
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = larger;
    }
}
