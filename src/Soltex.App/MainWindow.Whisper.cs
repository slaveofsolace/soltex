using System.IO;
using System.Security;
using System.Windows;
using Soltex.App.Views;
using Soltex.Whisper;
using Soltex.Whisper.Windows;

namespace Soltex.App;

public partial class MainWindow
{
    private readonly SemaphoreSlim _whisperDeviceRefreshGate = new(1, 1);
    private WhisperSettingsStore? _whisperSettingsStore;
    private WhisperSettings _whisperSettings = WhisperSettings.CreateDefault();
    private WhisperWasapiCaptureSource? _whisperCapture;
    private WhisperCaptureDeviceSnapshot _whisperDevices = new([]);
    private WhisperCaptureException? _whisperCaptureFailure;
    private CancellationTokenSource? _whisperCaptureCancellation;
    private Task _whisperCaptureDrained = Task.CompletedTask;

    private void InitializeWhisperCapture()
    {
        _whisperSettingsStore = new WhisperSettingsStore(
            Path.Combine(_runtime.DataRoot, "whisper-settings.json"));
        WhisperSettingsLoadResult loaded = _whisperSettingsStore.Load();
        _whisperSettings = loaded.Settings;
        _whisperCapture = new WhisperWasapiCaptureSource(_whisperSettings.InputDeviceId);
        _whisperCapture.InputLevelChanged += WhisperCapture_InputLevelChanged;
        _whisperCapture.DeviceSelectionChanged += WhisperCapture_DeviceSelectionChanged;

        WhisperPanel.DeviceRefreshRequested += WhisperPanel_DeviceRefreshRequested;
        WhisperPanel.InputDeviceRequested += WhisperPanel_InputDeviceRequested;
        WhisperPanel.MicrophoneTestRequested += WhisperPanel_MicrophoneTestRequested;
        WhisperPanel.MicrophoneTestStopRequested += WhisperPanel_MicrophoneTestStopRequested;
        WhisperPanel.UpdateCaptureDevices(
            _whisperDevices,
            _whisperSettings.InputDeviceId,
            _renderSmokeMode
                ? "Microphone discovery is skipped during render evidence."
                : "Refresh to enumerate Windows input devices.");
        UpdateWhisperReadiness();
    }

    private async Task RefreshWhisperCaptureDevicesAsync()
    {
        if (_renderSmokeMode || _whisperCapture is null)
        {
            return;
        }

        await _whisperDeviceRefreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            WhisperCaptureDeviceSnapshot devices =
                await _whisperCapture.EnumerateDevicesAsync(timeout.Token).ConfigureAwait(false);
            _whisperDevices = devices;
            _whisperCaptureFailure = devices.Devices.Count == 0
                ? new WhisperCaptureException(
                    WhisperCaptureFailureKind.NoDevice,
                    "Windows reported no available microphone.")
                : null;
            await Dispatcher.InvokeAsync(() =>
            {
                string detail = devices.Devices.Count == 0
                    ? "Windows reported no available microphone."
                    : $"{devices.Devices.Count} input device(s) available.";
                WhisperPanel.UpdateCaptureDevices(
                    devices,
                    _whisperSettings.InputDeviceId,
                    detail);
                UpdateWhisperReadiness();
            });
        }
        catch (WhisperCaptureException exception)
        {
            _whisperCaptureFailure = exception;
            await Dispatcher.InvokeAsync(() =>
            {
                WhisperPanel.UpdateCaptureDevices(
                    new WhisperCaptureDeviceSnapshot([]),
                    _whisperSettings.InputDeviceId,
                    WhisperRedaction.Sanitize(exception.Message, 160));
                UpdateWhisperReadiness();
            });
        }
        catch (OperationCanceledException) when (_shutdownStarted)
        {
            // Shutdown owns cancellation; the page must not surface it as a fault.
        }
        catch (OperationCanceledException)
        {
            _whisperCaptureFailure = new WhisperCaptureException(
                WhisperCaptureFailureKind.Unavailable,
                "Windows microphone discovery exceeded its five-second bound.");
            await Dispatcher.InvokeAsync(() =>
            {
                WhisperPanel.UpdateCaptureDevices(
                    new WhisperCaptureDeviceSnapshot([]),
                    _whisperSettings.InputDeviceId,
                    _whisperCaptureFailure.Message);
                UpdateWhisperReadiness();
            });
        }
        finally
        {
            _whisperDeviceRefreshGate.Release();
        }
    }

    private async void WhisperPanel_DeviceRefreshRequested(object? sender, EventArgs e)
    {
        await RefreshWhisperCaptureDevicesAsync();
    }

    private void WhisperPanel_InputDeviceRequested(
        object? sender,
        WhisperInputDeviceRequestedEventArgs e)
    {
        if (_whisperCapture is null)
        {
            return;
        }

        try
        {
            _whisperCapture.SelectInputDevice(e.DeviceId);
            WhisperSettingsDocument document = _whisperSettings.ToDocument();
            document.InputDeviceId = e.DeviceId;
            _whisperSettings = WhisperSettingsMigrator.Load(document).Settings;
            _whisperSettingsStore?.Save(_whisperSettings);
            _whisperCaptureFailure = null;
            WhisperPanel.SetMicrophoneTestState(
                running: false,
                e.DeviceId is null
                    ? "Choose a microphone before testing capture."
                    : "Microphone selection saved for this Windows account.");
            UpdateWhisperReadiness();
        }
        catch (Exception exception) when (IsExpectedWhisperSettingsFailure(exception))
        {
            WhisperPanel.SetMicrophoneTestState(
                running: false,
                "The microphone selection could not be saved.");
        }
    }

    private void WhisperPanel_MicrophoneTestRequested(object? sender, EventArgs e)
    {
        if (_whisperCapture is null || !_whisperCaptureDrained.IsCompleted)
        {
            return;
        }

        _whisperCaptureCancellation?.Dispose();
        _whisperCaptureCancellation = new CancellationTokenSource();
        _whisperCaptureDrained = RunWhisperMicrophoneTestAsync(
            _whisperCaptureCancellation.Token);
    }

    private void WhisperPanel_MicrophoneTestStopRequested(object? sender, EventArgs e) =>
        _whisperCapture?.CompleteCurrentCapture();

    private async Task RunWhisperMicrophoneTestAsync(CancellationToken cancellationToken)
    {
        if (_whisperCapture is null)
        {
            return;
        }

        WhisperPanel.SetMicrophoneTestState(
            running: true,
            "Listening for up to five seconds. Audio is discarded after this test.");
        ShowWhisperOverlayPreview();
        try
        {
            Task<WhisperAudioClip> capture = _whisperCapture.CaptureAsync(
                WhisperCaptureMode.PushToTalk,
                cancellationToken).AsTask();
            Task timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            Task completed = await Task.WhenAny(capture, timeout);
            if (ReferenceEquals(completed, timeout) && !cancellationToken.IsCancellationRequested)
            {
                _whisperCapture.CompleteCurrentCapture();
            }

            using WhisperAudioClip clip = await capture;
            _whisperCaptureFailure = null;
            WhisperPanel.SetMicrophoneTestState(
                running: false,
                $"Capture passed · {clip.Duration.TotalSeconds:F1} s · 16 kHz mono · audio discarded.");
            UpdateWhisperReadiness();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!_shutdownStarted)
            {
                WhisperPanel.SetMicrophoneTestState(
                    running: false,
                    "Microphone test cancelled; buffered audio was cleared.");
            }
        }
        catch (WhisperCaptureException exception)
        {
            _whisperCaptureFailure = exception;
            WhisperPanel.SetMicrophoneTestState(
                running: false,
                WhisperRedaction.Sanitize(exception.Message, 160));
            UpdateWhisperReadiness();
        }
        finally
        {
            _whisperOverlay?.Hide();
        }
    }

    private void WhisperCapture_InputLevelChanged(
        object? sender,
        WhisperInputLevelEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(() => _whisperOverlay?.SetInputLevel(e.Level));
    }

    private void WhisperCapture_DeviceSelectionChanged(
        object? sender,
        WhisperCaptureSelection selection)
    {
        if (!selection.UsedFallback)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() => WhisperPanel.SetMicrophoneTestState(
            running: true,
            $"The saved input is unavailable; using {selection.DeviceName} for this test."));
    }

    private void UpdateWhisperReadiness()
    {
        bool selectedDeviceAvailable = _whisperSettings.InputDeviceId is { Length: > 0 } id &&
            _whisperDevices.Devices.Any(device =>
                string.Equals(device.Id, id, StringComparison.Ordinal));
        WhisperReadinessInputs inputs = new(
            FeatureEnabled: _whisperSettings.Enabled,
            MicrophoneSelected: selectedDeviceAvailable,
            MicrophonePermissionGranted: true,
            ShortcutsRegistered: false,
            ShortcutRegistrationError: null,
            TranscriberConfigured: false,
            TranscriberCredentialAvailable: false,
            TargetInspectionAvailable: false,
            AutoSendEnabled: _whisperSettings.AutoSendEnabled,
            AutoSendWarningAccepted: _whisperSettings.AutoSendWarningAccepted,
            EnabledAutoSendProfileCount: 0);
        WhisperPanel.UpdateReadiness(WhisperCaptureReadiness.Apply(
            inputs,
            _whisperCaptureFailure,
            selectedDeviceAvailable));
    }

    private async Task DisposeWhisperCaptureAsync()
    {
        _whisperCaptureCancellation?.Cancel();
        _whisperCapture?.CompleteCurrentCapture();
        if (_whisperCapture is not null)
        {
            _whisperCapture.InputLevelChanged -= WhisperCapture_InputLevelChanged;
            _whisperCapture.DeviceSelectionChanged -= WhisperCapture_DeviceSelectionChanged;
            await _whisperCapture.DisposeAsync();
            _whisperCapture = null;
        }

        _whisperCaptureCancellation?.Dispose();
        _whisperCaptureCancellation = null;
        _whisperDeviceRefreshGate.Dispose();
    }

    private static bool IsExpectedWhisperSettingsFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            InvalidOperationException or ArgumentException;
}
