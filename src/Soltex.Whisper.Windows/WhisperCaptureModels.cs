using System.Collections.ObjectModel;
using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

public enum WhisperCaptureFailureKind
{
    PermissionDenied,
    NoDevice,
    DeviceInUse,
    DeviceDisconnected,
    UnsupportedFormat,
    NoAudioCaptured,
    Unavailable
}

public sealed class WhisperCaptureException : InvalidOperationException
{
    public WhisperCaptureException(WhisperCaptureFailureKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public WhisperCaptureException(
        WhisperCaptureFailureKind kind,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public WhisperCaptureFailureKind Kind { get; }
}

public sealed record WhisperCaptureDevice(
    string Id,
    string Name,
    bool IsDefault);

public sealed class WhisperCaptureDeviceSnapshot
{
    public WhisperCaptureDeviceSnapshot(IEnumerable<WhisperCaptureDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        Devices = new ReadOnlyCollection<WhisperCaptureDevice>(devices.ToArray());
    }

    public ReadOnlyCollection<WhisperCaptureDevice> Devices { get; }
}

public sealed record WhisperCaptureSelection(
    string DeviceId,
    string DeviceName,
    bool UsedFallback);

public sealed class WhisperInputLevelEventArgs : EventArgs
{
    public WhisperInputLevelEventArgs(double level)
    {
        Level = Math.Clamp(level, 0, 1);
    }

    public double Level { get; }
}

public static class WhisperCaptureReadiness
{
    public static WhisperReadinessInputs Apply(
        WhisperReadinessInputs inputs,
        WhisperCaptureException? failure,
        bool hasSelectedDevice)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (failure?.Kind == WhisperCaptureFailureKind.PermissionDenied)
        {
            return inputs with
            {
                MicrophonePermissionGranted = false,
                MicrophoneSelected = hasSelectedDevice,
                MicrophoneError = null
            };
        }

        if (failure?.Kind == WhisperCaptureFailureKind.NoDevice)
        {
            return inputs with
            {
                MicrophonePermissionGranted = true,
                MicrophoneSelected = false,
                MicrophoneError = null
            };
        }

        return inputs with
        {
            MicrophonePermissionGranted = true,
            MicrophoneSelected = hasSelectedDevice,
            MicrophoneError = failure is null ? null : Describe(failure.Kind)
        };
    }

    private static string Describe(WhisperCaptureFailureKind kind) => kind switch
    {
        WhisperCaptureFailureKind.DeviceInUse =>
            "The selected microphone is in exclusive use by another application.",
        WhisperCaptureFailureKind.DeviceDisconnected =>
            "The selected microphone disconnected and no fallback input was available.",
        WhisperCaptureFailureKind.UnsupportedFormat =>
            "The selected microphone uses a format Soltex cannot normalize safely.",
        WhisperCaptureFailureKind.NoAudioCaptured =>
            "The microphone returned no audio before capture stopped.",
        _ => "Windows audio capture is currently unavailable."
    };
}
