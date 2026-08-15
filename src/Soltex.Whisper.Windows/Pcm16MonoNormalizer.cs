using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Soltex.Whisper.Windows;

internal sealed class Pcm16MonoNormalizer
{
    private const ushort WaveFormatPcm = 1;
    private const ushort WaveFormatIeeeFloat = 3;
    private const ushort WaveFormatExtensible = 0xFFFE;
    private readonly WaveFormatEx _format;
    private readonly bool _isFloat;
    private long _phase;

    internal Pcm16MonoNormalizer(IntPtr formatPointer)
    {
        if (formatPointer == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(formatPointer));
        }

        _format = Marshal.PtrToStructure<WaveFormatEx>(formatPointer);
        if (_format.Channels is < 1 or > 8 ||
            _format.SamplesPerSecond is < 8_000 or > 192_000 ||
            _format.BlockAlign is < 1 or > 64)
        {
            throw Unsupported();
        }

        Guid? subFormat = _format.FormatTag == WaveFormatExtensible && _format.ExtraSize >= 22
            ? Marshal.PtrToStructure<Guid>(IntPtr.Add(formatPointer, 24))
            : null;
        _isFloat = _format.FormatTag == WaveFormatIeeeFloat ||
            subFormat == WasapiNative.FloatSubFormat;
        bool isPcm = _format.FormatTag == WaveFormatPcm ||
            subFormat == WasapiNative.PcmSubFormat;
        if ((!_isFloat && !isPcm) ||
            (_isFloat && _format.BitsPerSample != 32) ||
            (!isPcm && !_isFloat) ||
            (isPcm && _format.BitsPerSample is not (8 or 16 or 24 or 32)))
        {
            throw Unsupported();
        }

        int bytesPerChannel = _format.BitsPerSample / 8;
        if (bytesPerChannel * _format.Channels > _format.BlockAlign)
        {
            throw Unsupported();
        }
    }

    internal byte[] Normalize(IntPtr source, uint frameCount, bool silent)
    {
        if (frameCount == 0 || frameCount > 384_000)
        {
            throw new WhisperCaptureException(
                WhisperCaptureFailureKind.Unavailable,
                "Windows returned an invalid microphone packet length.");
        }

        int outputSamples = checked((int)Math.Ceiling(
            frameCount * WhisperWasapiCaptureSource.OutputSampleRateHz /
            (double)_format.SamplesPerSecond) + 1);
        byte[] output = GC.AllocateUninitializedArray<byte>(checked(outputSamples * sizeof(short)));
        try
        {
            int outputOffset = 0;
            if (silent)
            {
                output.AsSpan().Clear();
                for (uint frame = 0; frame < frameCount; frame++)
                {
                    WriteResampled(0, output, ref outputOffset);
                }
            }
            else
            {
                int inputLength = checked((int)(frameCount * _format.BlockAlign));
                byte[] input = GC.AllocateUninitializedArray<byte>(inputLength);
                try
                {
                    Marshal.Copy(source, input, 0, inputLength);
                    for (uint frame = 0; frame < frameCount; frame++)
                    {
                        float mixed = ReadMono(input, checked((int)(frame * _format.BlockAlign)));
                        short sample = (short)Math.Clamp(
                            Math.Round(mixed * short.MaxValue),
                            short.MinValue,
                            short.MaxValue);
                        WriteResampled(sample, output, ref outputOffset);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(input);
                }
            }

            if (outputOffset == 0)
            {
                CryptographicOperations.ZeroMemory(output);
                return [];
            }

            if (outputOffset == output.Length)
            {
                return output;
            }

            byte[] exact = GC.AllocateUninitializedArray<byte>(outputOffset);
            output.AsSpan(0, outputOffset).CopyTo(exact);
            CryptographicOperations.ZeroMemory(output);
            return exact;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(output);
            throw;
        }
    }

    private float ReadMono(ReadOnlySpan<byte> input, int frameOffset)
    {
        int bytesPerChannel = _format.BitsPerSample / 8;
        double total = 0;
        for (int channel = 0; channel < _format.Channels; channel++)
        {
            int offset = frameOffset + (channel * bytesPerChannel);
            total += _isFloat
                ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(input[offset..]))
                : ReadPcm(input[offset..], _format.BitsPerSample);
        }

        return (float)Math.Clamp(total / _format.Channels, -1, 1);
    }

    private static double ReadPcm(ReadOnlySpan<byte> input, ushort bitsPerSample) =>
        bitsPerSample switch
        {
            8 => (input[0] - 128) / 128d,
            16 => BinaryPrimitives.ReadInt16LittleEndian(input) / 32768d,
            24 => ReadInt24(input) / 8388608d,
            32 => BinaryPrimitives.ReadInt32LittleEndian(input) / 2147483648d,
            _ => throw Unsupported()
        };

    private static int ReadInt24(ReadOnlySpan<byte> input)
    {
        int value = input[0] | (input[1] << 8) | (input[2] << 16);
        return (value & 0x0080_0000) != 0 ? value | unchecked((int)0xFF00_0000) : value;
    }

    private void WriteResampled(short sample, byte[] output, ref int offset)
    {
        _phase += WhisperWasapiCaptureSource.OutputSampleRateHz;
        while (_phase >= _format.SamplesPerSecond)
        {
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset), sample);
            offset += sizeof(short);
            _phase -= _format.SamplesPerSecond;
        }
    }

    private static WhisperCaptureException Unsupported() => new(
        WhisperCaptureFailureKind.UnsupportedFormat,
        "The selected microphone uses a format Soltex cannot normalize safely.");
}
