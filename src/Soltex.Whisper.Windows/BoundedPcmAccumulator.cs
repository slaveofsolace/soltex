using System.Buffers;
using System.Security.Cryptography;

namespace Soltex.Whisper.Windows;

internal sealed class BoundedPcmAccumulator : IDisposable
{
    private const int ChunkSize = 64 * 1024;
    private readonly int _maximumBytes;
    private readonly List<byte[]> _chunks = [];
    private int _length;
    private bool _detached;

    internal BoundedPcmAccumulator(int maximumBytes)
    {
        if (maximumBytes <= 0 || (maximumBytes & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        _maximumBytes = maximumBytes;
    }

    internal int Length => _length;

    internal int Remaining => _maximumBytes - _length;

    internal void Append(ReadOnlySpan<byte> pcm16)
    {
        ObjectDisposedException.ThrowIf(_detached, this);

        if ((pcm16.Length & 1) != 0)
        {
            throw new ArgumentException("PCM16 input must contain complete samples.", nameof(pcm16));
        }

        if (pcm16.Length > Remaining)
        {
            throw new WhisperCaptureException(
                WhisperCaptureFailureKind.Unavailable,
                "The bounded Whisper capture buffer reached its 20-minute limit.");
        }

        int copied = 0;
        while (copied < pcm16.Length)
        {
            int chunkOffset = _length % ChunkSize;
            if (chunkOffset == 0)
            {
                _chunks.Add(ArrayPool<byte>.Shared.Rent(ChunkSize));
            }

            byte[] chunk = _chunks[^1];
            int available = Math.Min(ChunkSize - chunkOffset, pcm16.Length - copied);
            pcm16.Slice(copied, available).CopyTo(chunk.AsSpan(chunkOffset, available));
            copied += available;
            _length += available;
        }
    }

    internal byte[] Detach()
    {
        ObjectDisposedException.ThrowIf(_detached, this);

        byte[] result = GC.AllocateUninitializedArray<byte>(_length);
        int copied = 0;
        foreach (byte[] chunk in _chunks)
        {
            int count = Math.Min(ChunkSize, _length - copied);
            chunk.AsSpan(0, count).CopyTo(result.AsSpan(copied, count));
            copied += count;
        }

        _detached = true;
        ReleaseChunks();
        return result;
    }

    public void Dispose()
    {
        _detached = true;
        ReleaseChunks();
    }

    private void ReleaseChunks()
    {
        foreach (byte[] chunk in _chunks)
        {
            CryptographicOperations.ZeroMemory(chunk);
            ArrayPool<byte>.Shared.Return(chunk, clearArray: false);
        }

        _chunks.Clear();
        _length = 0;
    }
}
