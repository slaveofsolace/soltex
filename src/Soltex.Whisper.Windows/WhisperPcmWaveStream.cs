using System.Buffers.Binary;

namespace Soltex.Whisper.Windows;

/// <summary>
/// Read-only RIFF/WAVE projection over the clip's existing owned PCM memory. Only the
/// 44-byte header is allocated; audio bytes are never cloned into another buffer.
/// </summary>
internal sealed class WhisperPcmWaveStream : Stream
{
    internal const int HeaderBytes = 44;

    private readonly ReadOnlyMemory<byte> _pcm16;
    private readonly byte[] _header;
    private long _position;
    private bool _disposed;

    internal WhisperPcmWaveStream(
        ReadOnlyMemory<byte> pcm16,
        int sampleRateHz,
        short channelCount)
    {
        if (pcm16.IsEmpty || pcm16.Length % sizeof(short) != 0)
        {
            throw new ArgumentException(
                "PCM16 audio must contain a non-empty whole number of samples.",
                nameof(pcm16));
        }

        if (sampleRateHz is < 8_000 or > 192_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }

        if (channelCount is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        }

        _pcm16 = pcm16;
        _header = CreateHeader(pcm16.Length, sampleRateHz, channelCount);
    }

    public override bool CanRead => !_disposed;

    public override bool CanSeek => !_disposed;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            ThrowIfDisposed();
            return HeaderBytes + (long)_pcm16.Length;
        }
    }

    public override long Position
    {
        get
        {
            ThrowIfDisposed();
            return _position;
        }
        set
        {
            ThrowIfDisposed();
            if (value < 0 || value > Length)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
        {
            throw new ArgumentException("The destination buffer is too small.", nameof(buffer));
        }

        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        if (buffer.IsEmpty || _position >= Length)
        {
            return 0;
        }

        int written = 0;
        if (_position < HeaderBytes)
        {
            int headerOffset = checked((int)_position);
            int headerCount = Math.Min(buffer.Length, HeaderBytes - headerOffset);
            _header.AsSpan(headerOffset, headerCount).CopyTo(buffer);
            _position += headerCount;
            written += headerCount;
            buffer = buffer[headerCount..];
        }

        if (!buffer.IsEmpty && _position >= HeaderBytes)
        {
            int pcmOffset = checked((int)(_position - HeaderBytes));
            int pcmCount = Math.Min(buffer.Length, _pcm16.Length - pcmOffset);
            _pcm16.Span.Slice(pcmOffset, pcmCount).CopyTo(buffer);
            _position += pcmCount;
            written += pcmCount;
        }

        return written;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();
        long next = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        Position = next;
        return next;
    }

    public override void Flush()
    {
        ThrowIfDisposed();
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException("The PCM projection is read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("The PCM projection is read-only.");

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            Array.Clear(_header);
            _disposed = true;
        }

        base.Dispose(disposing);
    }

    private static byte[] CreateHeader(int pcmBytes, int sampleRateHz, short channelCount)
    {
        byte[] header = new byte[HeaderBytes];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(4),
            checked((uint)(36L + pcmBytes)));
        "WAVE"u8.CopyTo(header.AsSpan(8));
        "fmt "u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22), checked((ushort)channelCount));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), checked((uint)sampleRateHz));
        uint byteRate = checked((uint)(sampleRateHz * channelCount * sizeof(short)));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(32),
            checked((ushort)(channelCount * sizeof(short))));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34), 16);
        "data"u8.CopyTo(header.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), checked((uint)pcmBytes));
        return header;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
