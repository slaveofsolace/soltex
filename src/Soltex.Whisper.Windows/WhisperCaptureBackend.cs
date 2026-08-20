using System.Security.Cryptography;

namespace Soltex.Whisper.Windows;

internal interface IWhisperCaptureBackendFactory
{
    ValueTask<WhisperCaptureDeviceSnapshot> EnumerateAsync(CancellationToken cancellationToken);

    ValueTask<IWhisperCaptureBackend> OpenAsync(
        string? requestedDeviceId,
        CancellationToken cancellationToken);
}

internal interface IWhisperCaptureBackend : IAsyncDisposable
{
    WhisperCaptureSelection Selection { get; }

    ValueTask<WhisperPcmPacket> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class WhisperPcmPacket : IDisposable
{
    private byte[]? _buffer;

    internal WhisperPcmPacket(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length == 0 || (buffer.Length & 1) != 0)
        {
            throw new ArgumentException("A PCM16 packet must contain complete samples.", nameof(buffer));
        }

        _buffer = buffer;
    }

    internal ReadOnlyMemory<byte> Memory =>
        _buffer ?? throw new ObjectDisposedException(nameof(WhisperPcmPacket));

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
}
