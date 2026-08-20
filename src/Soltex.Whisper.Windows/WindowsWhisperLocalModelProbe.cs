using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Soltex.Whisper.Windows;

/// <summary>
/// Content-free evidence from one explicit owner-controlled local-model probe.
/// No audio, transcript, vocabulary, target text, or local path crosses this boundary.
/// </summary>
public sealed record WhisperLocalModelProbeResult(
    double AudioDurationMilliseconds,
    double InitialTranscriptionMilliseconds,
    double InitialUnloadMilliseconds,
    double RestartTranscriptionMilliseconds,
    double CancellationMilliseconds,
    double CancellationUnloadMilliseconds,
    long WorkingSetBeforeBytes,
    long PeakWorkingSetBytes,
    long WorkingSetAfterBytes,
    long ModelBytes,
    bool RestartSucceeded,
    bool CancellationObserved);

/// <summary>
/// Runs a bounded, opt-in proof against the installed local model. This is an
/// evidence adapter, not a normal application startup path or a policy authority.
/// </summary>
public static class WindowsWhisperLocalModelProbe
{
    public const int MaximumFixtureBytes = 8 * 1_024 * 1_024;
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromMinutes(5);

    public static async Task<WhisperLocalModelProbeResult> RunAsync(
        string productDataRoot,
        string audioFixturePath,
        string expectedAudioSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productDataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFixturePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudioSha256);
        cancellationToken.ThrowIfCancellationRequested();

        using WhisperAudioClip clip = await LoadFixtureAsync(
                audioFixturePath,
                expectedAudioSha256,
                cancellationToken)
            .ConfigureAwait(false);
        await using WindowsWhisperLocalModelManager manager = new(productDataRoot);
        WhisperModelStatus model = await manager
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        WhisperLocalModelArtifact artifact = WhisperLocalModelArtifact.TurboQ5Cpu;
        if (!model.IsVerified ||
            model.InstalledBytes != artifact.ExpectedBytes ||
            model.ExpectedBytes != artifact.ExpectedBytes)
        {
            throw new InvalidOperationException(
                "The exact verified local transcription model is unavailable.");
        }

        await using WindowsWhisperLocalTranscriber transcriber = new(manager);
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        long workingSetBefore = process.WorkingSet64;

        Stopwatch initial = Stopwatch.StartNew();
        string transcript = await TranscribeAsync(
                transcriber,
                clip,
                "packaged-model-initial",
                cancellationToken)
            .ConfigureAwait(false);
        initial.Stop();
        transcript = string.Empty;

        Stopwatch initialUnload = Stopwatch.StartNew();
        await transcriber.UnloadAsync(cancellationToken).ConfigureAwait(false);
        initialUnload.Stop();

        Stopwatch restart = Stopwatch.StartNew();
        string restartedTranscript = await TranscribeAsync(
                transcriber,
                clip,
                "packaged-model-restart",
                cancellationToken)
            .ConfigureAwait(false);
        restart.Stop();
        restartedTranscript = string.Empty;
        await transcriber.UnloadAsync(cancellationToken).ConfigureAwait(false);

        using CancellationTokenSource cancellationProbe =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationProbe.CancelAfter(TimeSpan.FromMilliseconds(100));
        Stopwatch cancellation = Stopwatch.StartNew();
        bool cancellationObserved = false;
        try
        {
            string canceledTranscript = await TranscribeAsync(
                    transcriber,
                    clip,
                    "packaged-model-cancellation",
                    cancellationProbe.Token)
                .ConfigureAwait(false);
            canceledTranscript = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationProbe.IsCancellationRequested)
        {
            cancellationObserved = true;
        }

        cancellation.Stop();
        if (!cancellationObserved)
        {
            throw new InvalidOperationException(
                "The local transcription runtime did not honor the bounded cancellation probe.");
        }

        Stopwatch cancellationUnload = Stopwatch.StartNew();
        await transcriber.UnloadAsync(cancellationToken).ConfigureAwait(false);
        cancellationUnload.Stop();

        process.Refresh();
        return new WhisperLocalModelProbeResult(
            clip.Duration.TotalMilliseconds,
            initial.Elapsed.TotalMilliseconds,
            initialUnload.Elapsed.TotalMilliseconds,
            restart.Elapsed.TotalMilliseconds,
            cancellation.Elapsed.TotalMilliseconds,
            cancellationUnload.Elapsed.TotalMilliseconds,
            workingSetBefore,
            process.PeakWorkingSet64,
            process.WorkingSet64,
            model.InstalledBytes,
            RestartSucceeded: true,
            CancellationObserved: cancellationObserved);
    }

    internal static async ValueTask<WhisperAudioClip> LoadFixtureAsync(
        string configuredPath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(configuredPath);
        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "The owner-selected local-model fixture is a reparse point.");
        }

        using SafeFileHandle handle = File.OpenHandle(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = RandomAccess.GetLength(handle);
        if (length is < 44 or > MaximumFixtureBytes)
        {
            throw new InvalidDataException(
                "The owner-selected local-model fixture is outside its byte bound.");
        }

        byte[] wave = GC.AllocateUninitializedArray<byte>(checked((int)length));
        try
        {
            int received = 0;
            while (received < wave.Length)
            {
                int read = await RandomAccess.ReadAsync(
                        handle,
                        wave.AsMemory(received),
                        received,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new InvalidDataException(
                        "The owner-selected local-model fixture changed or was truncated.");
                }

                received = checked(received + read);
            }

            if (RandomAccess.GetLength(handle) != length)
            {
                throw new InvalidDataException(
                    "The owner-selected local-model fixture changed during the bounded read.");
            }

            byte[] expected;
            try
            {
                expected = Convert.FromHexString(expectedSha256);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    "The owner-selected local-model fixture digest is invalid.",
                    exception);
            }

            byte[] observed = SHA256.HashData(wave);
            try
            {
                if (expected.Length != observed.Length ||
                    !CryptographicOperations.FixedTimeEquals(expected, observed))
                {
                    throw new InvalidDataException(
                        "The owner-selected local-model fixture did not match its pinned digest.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expected);
                CryptographicOperations.ZeroMemory(observed);
            }

            return ParsePcm16MonoWave(wave);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wave);
        }
    }

    internal static WhisperAudioClip ParsePcm16MonoWave(ReadOnlySpan<byte> wave)
    {
        if (wave.Length < 44 ||
            !wave[..4].SequenceEqual("RIFF"u8) ||
            !wave.Slice(8, 4).SequenceEqual("WAVE"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(wave.Slice(4, 4)) != wave.Length - 8)
        {
            throw new InvalidDataException(
                "The local-model fixture is not a bounded RIFF WAVE file.");
        }

        ushort format = 0;
        ushort channels = 0;
        uint sampleRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        int dataOffset = -1;
        int dataLength = -1;
        bool formatSeen = false;
        int offset = 12;
        while (offset <= wave.Length - 8)
        {
            ReadOnlySpan<byte> id = wave.Slice(offset, 4);
            uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(
                wave.Slice(offset + 4, sizeof(uint)));
            if (declaredLength > int.MaxValue)
            {
                throw new InvalidDataException(
                    "The local-model fixture contains an oversized WAVE chunk.");
            }

            int chunkLength = checked((int)declaredLength);
            int contentOffset = checked(offset + 8);
            int contentEnd = checked(contentOffset + chunkLength);
            if (contentEnd > wave.Length)
            {
                throw new InvalidDataException(
                    "The local-model fixture contains a truncated WAVE chunk.");
            }

            if (id.SequenceEqual("fmt "u8))
            {
                if (formatSeen || chunkLength < 16)
                {
                    throw new InvalidDataException(
                        "The local-model fixture has an invalid format chunk.");
                }

                formatSeen = true;
                ReadOnlySpan<byte> value = wave.Slice(contentOffset, chunkLength);
                format = BinaryPrimitives.ReadUInt16LittleEndian(value);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(value[2..]);
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(value[4..]);
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(value[12..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(value[14..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (dataOffset >= 0)
                {
                    throw new InvalidDataException(
                        "The local-model fixture contains multiple audio data chunks.");
                }

                dataOffset = contentOffset;
                dataLength = chunkLength;
            }

            int paddedEnd = checked(contentEnd + (chunkLength & 1));
            if (paddedEnd > wave.Length)
            {
                throw new InvalidDataException(
                    "The local-model fixture has invalid WAVE padding.");
            }

            offset = paddedEnd;
        }

        if (offset != wave.Length ||
            !formatSeen ||
            format != 1 ||
            channels != 1 ||
            sampleRate != WhisperWasapiCaptureSource.OutputSampleRateHz ||
            blockAlign != sizeof(short) ||
            bitsPerSample != 16 ||
            dataOffset < 0 ||
            dataLength < WindowsWhisperLocalTranscriber.MinimumPcmSampleCount * sizeof(short) ||
            dataLength % sizeof(short) != 0)
        {
            throw new InvalidDataException(
                "The local-model fixture must contain bounded 16 kHz mono PCM16 audio.");
        }

        byte[] pcm = wave.Slice(dataOffset, dataLength).ToArray();
        TimeSpan duration = TimeSpan.FromSeconds(
            dataLength / (double)(sampleRate * blockAlign));
        return WhisperAudioClip.CreateOwned(pcm, checked((int)sampleRate), channels, duration);
    }

    private static async ValueTask<string> TranscribeAsync(
        WindowsWhisperLocalTranscriber transcriber,
        WhisperAudioClip clip,
        string evidenceContext,
        CancellationToken cancellationToken)
    {
        string transcript = await transcriber
            .TranscribeAsync(
                clip,
                new WhisperTranscriptionContext(
                    WhisperCaptureMode.PushToTalk,
                    "en",
                    evidenceContext,
                    "default",
                    Array.Empty<string>()),
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            throw new InvalidOperationException(
                "The local transcription runtime returned an empty result.");
        }

        return transcript;
    }
}
