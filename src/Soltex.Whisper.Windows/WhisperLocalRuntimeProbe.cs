using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Soltex.Whisper.Windows;

/// <summary>
/// Content-free package evidence for the local CPU runtime. This probe loads only
/// the native runtime; it never opens a model, microphone, transcript, or target.
/// </summary>
public sealed record WhisperLocalRuntimeProbeResult(
    string ProviderId,
    string RuntimeId,
    bool Available);

public static class WhisperLocalRuntimeProbe
{
    private static readonly object RuntimeConfigurationLock = new();

    public static WhisperLocalRuntimeProbeResult Run()
    {
        lock (RuntimeConfigurationLock)
        {
            try
            {
                RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
                string? runtimeInformation = WhisperFactory.GetRuntimeInfo();
                if (string.IsNullOrWhiteSpace(runtimeInformation) ||
                    RuntimeOptions.LoadedLibrary != RuntimeLibrary.Cpu)
                {
                    throw new InvalidOperationException(
                        "The packaged local CPU transcription runtime is unavailable.");
                }

                return new WhisperLocalRuntimeProbeResult(
                    WhisperLocalModelDefaults.ProviderId,
                    WhisperLocalModelDefaults.RuntimeId,
                    Available: true);
            }
            catch (WhisperLocalTranscriptionException)
            {
                throw;
            }
            catch (Exception exception) when (exception is DllNotFoundException or
                                               BadImageFormatException or
                                               InvalidOperationException or
                                               TypeInitializationException)
            {
                throw new WhisperLocalTranscriptionException(
                    WhisperLocalTranscriptionFailureKind.RuntimeUnavailable,
                    "The packaged local CPU transcription runtime could not be loaded.");
            }
        }
    }
}
