using System.Buffers;
using System.Text;

namespace ClaySharp.Raylib;

internal unsafe delegate TResult Utf8Func<TResult>(sbyte* value);

internal unsafe delegate void Utf8Action(sbyte* value);

internal unsafe sealed class Utf8ScratchBuffer : IDisposable
{
    private byte[] _buffer;

    public Utf8ScratchBuffer(int initialCapacity = 256)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 16));
    }

    public TResult WithCString<TResult>(ReadOnlySpan<char> text, Utf8Func<TResult> callback)
    {
        var byteCount = Encoding.UTF8.GetByteCount(text);
        EnsureCapacity(byteCount + 1);
        var written = Encoding.UTF8.GetBytes(text, _buffer.AsSpan());
        _buffer[written] = 0;

        fixed (byte* pointer = _buffer)
        {
            return callback((sbyte*)pointer);
        }
    }

    public void WithCString(ReadOnlySpan<char> text, Utf8Action callback)
    {
        var byteCount = Encoding.UTF8.GetByteCount(text);
        EnsureCapacity(byteCount + 1);
        var written = Encoding.UTF8.GetBytes(text, _buffer.AsSpan());
        _buffer[written] = 0;

        fixed (byte* pointer = _buffer)
        {
            callback((sbyte*)pointer);
        }
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
    }

    private void EnsureCapacity(int required)
    {
        if (_buffer.Length >= required)
        {
            return;
        }

        var replacement = ArrayPool<byte>.Shared.Rent(Math.Max(required, _buffer.Length * 2));
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = replacement;
    }
}
