using System.ComponentModel;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Windows;
using Soltex.App.Views;
using Soltex.Security;
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
    private WindowsWhisperLocalModelManager? _whisperModelManager;
    private WhisperModelStatus? _whisperModelStatus;
    private CancellationTokenSource? _whisperModelCancellation;
    private Task _whisperModelDrained = Task.CompletedTask;
    private WindowsWhisperLocalTranscriber? _whisperTranscriber;
    private WindowsWhisperTargetInspector? _whisperTargetInspector;
    private WindowsWhisperTextDelivery? _whisperTextDelivery;
    private WindowsWhisperVerifiedSubmitter? _whisperVerifiedSubmitter;
    private WhisperSessionRunner? _whisperSessionRunner;
    private Task _whisperSessionDrained = Task.CompletedTask;
    private CancellationTokenSource? _whisperSessionCancellation;
    private WhisperCaptureMode? _whisperSessionMode;
    private bool _whisperHandsFreeLocked;
    private string? _whisperSessionTargetProcess;
    private bool _whisperSessionTargetKnown;
    private WhisperDeliveryKind _whisperSessionDeliveryKind;
    private readonly SemaphoreSlim _whisperShortcutGate = new(1, 1);
    private readonly WhisperShortcutGestureInterpreter _whisperShortcutGestures = new();
    private readonly CancellationTokenSource _whisperRuntimeCancellation = new();
    private readonly BoundedWhisperHistory _whisperHistory = new(WhisperLimits.MaximumHistoryEntries);
    private readonly WhisperOwnerAcceptanceTracker _whisperOwnerAcceptance = new();
    private readonly SemaphoreSlim _whisperHistoryGate = new(1, 1);
    private IWhisperHistoryRetentionStore? _whisperHistoryStore;
    private Task _whisperHistoryDrained = Task.CompletedTask;
    private WindowsWhisperShortcutHost? _whisperShortcutHost;
    private Task _whisperShortcutDrained = Task.CompletedTask;
    private string? _whisperShortcutError;

    private void InitializeWhisperCapture()
    {
        _whisperSettingsStore = new WhisperSettingsStore(
            Path.Combine(_runtime.DataRoot, "whisper-settings.json"));
        WhisperSettingsLoadResult loaded = _whisperSettingsStore.Load();
        _whisperSettings = loaded.Settings;
        _whisperHistoryStore = new WindowsWhisperHistoryRetentionStore(
            Path.Combine(_runtime.DataRoot, "state"));
        string productDataRoot = ProductDataRootResolver.ResolveDefault().ProductRoot;
        _whisperModelManager = new WindowsWhisperLocalModelManager(productDataRoot);
        _whisperCapture = new WhisperWasapiCaptureSource(_whisperSettings.InputDeviceId);
        _whisperTranscriber = new WindowsWhisperLocalTranscriber(_whisperModelManager);
        _whisperTargetInspector = new WindowsWhisperTargetInspector();
        _whisperTextDelivery = new WindowsWhisperTextDelivery(_whisperTargetInspector);
        _whisperVerifiedSubmitter = new WindowsWhisperVerifiedSubmitter(
            _whisperTargetInspector);
        _whisperSessionRunner = new WhisperSessionRunner(
            _whisperCapture,
            _whisperTranscriber,
            _whisperTargetInspector,
            _whisperTextDelivery,
            _whisperVerifiedSubmitter);
        _whisperSessionRunner.StateChanged += WhisperSessionRunner_StateChanged;
        _whisperCapture.InputLevelChanged += WhisperCapture_InputLevelChanged;
        _whisperCapture.DeviceSelectionChanged += WhisperCapture_DeviceSelectionChanged;
        DpiChanged += MainWindow_WhisperDpiChanged;

        WhisperPanel.DeviceRefreshRequested += WhisperPanel_DeviceRefreshRequested;
        WhisperPanel.InputDeviceRequested += WhisperPanel_InputDeviceRequested;
        WhisperPanel.MicrophoneTestRequested += WhisperPanel_MicrophoneTestRequested;
        WhisperPanel.MicrophoneTestStopRequested += WhisperPanel_MicrophoneTestStopRequested;
        WhisperPanel.FeatureEnabledRequested += WhisperPanel_FeatureEnabledRequested;
        WhisperPanel.LocalProviderSelectRequested += WhisperPanel_LocalProviderSelectRequested;
        WhisperPanel.ModelActionRequested += WhisperPanel_ModelActionRequested;
        WhisperPanel.ModelDeleteRequested += WhisperPanel_ModelDeleteRequested;
        WhisperPanel.PersonalizationRequested += WhisperPanel_PersonalizationRequested;
        WhisperPanel.LibraryRequested += WhisperPanel_LibraryRequested;
        WhisperPanel.HistoryEntryDeleteRequested += WhisperPanel_HistoryEntryDeleteRequested;
        WhisperPanel.HistoryClearRequested += WhisperPanel_HistoryClearRequested;
        WhisperPanel.PrivacyRequested += WhisperPanel_PrivacyRequested;
        WhisperPanel.OwnerAcceptanceRequested += WhisperPanel_OwnerAcceptanceRequested;
        WhisperPanel.UpdateCaptureDevices(
            _whisperDevices,
            _whisperSettings.InputDeviceId,
            _renderSmokeMode
                ? "Microphone discovery is skipped during render evidence."
                : "Refresh to enumerate Windows input devices.");
        WhisperPanel.SetFeatureState(
            _whisperSettings.Enabled,
            updating: false,
            shortcutsRegistered: false,
            _whisperSettings.Enabled
                ? "Whisper is on. Windows shortcut registration will be checked at startup."
                : "Whisper is off. No global shortcuts are registered.");
        WhisperPanel.SetLocalModelStatus(
            _whisperSettings,
            CreateUncheckedWhisperModelStatus(),
            operationRunning: false,
            progress: 0,
            _renderSmokeMode
                ? "Model verification is skipped during deterministic render evidence."
                : "Checking exact-owned local model state. No download starts automatically.");
        WhisperPanel.SetPersonalization(
            _whisperSettings,
            loaded.LoadedVersion == 0
                ? "Shipped defaults are ready for this Windows account."
                : loaded.IsClean
                ? "Saved locally for this Windows account."
                : "Unsafe or outdated fields were repaired to safe values.");
        WhisperPanel.SetLibrary(
            _whisperSettings,
            loaded.LoadedVersion == 0
                ? "Shipped defaults are ready for this Windows account."
                : loaded.IsClean
                    ? "Saved locally for this Windows account."
                    : "Unsafe or outdated fields were repaired to safe values.");
        WhisperPanel.SetPrivacy(
            _whisperSettings,
            loaded.LoadedVersion == 0
                ? "Safe defaults are active."
                : loaded.IsClean
                    ? "Privacy settings loaded for this Windows account."
                    : "Unsafe or outdated fields were repaired to safe values.");
        UpdateWhisperHistoryView();
        UpdateWhisperReadiness();
    }

    private async void WhisperPanel_HistoryEntryDeleteRequested(
        object? sender,
        WhisperHistoryEntryRequestedEventArgs e)
    {
        if (_shutdownStarted)
        {
            return;
        }

        _whisperHistoryDrained = DeleteWhisperHistoryEntryAsync(
            e.Entry,
            _whisperRuntimeCancellation.Token);
        await _whisperHistoryDrained;
    }

    private async void WhisperPanel_HistoryClearRequested(object? sender, EventArgs e)
    {
        if (_shutdownStarted)
        {
            return;
        }

        _whisperHistoryDrained = ClearWhisperHistoryAsync(
            _whisperRuntimeCancellation.Token);
        await _whisperHistoryDrained;
    }

    private void UpdateWhisperHistoryView(string? detail = null)
    {
        WhisperPanel.SetHistory(
            _whisperHistory.CreateSnapshot(),
            _whisperSettings.HistoryMode,
            detail ?? (_whisperSettings.HistoryMode == WhisperHistoryMode.EncryptedDisk
                ? $"Encrypted for {_whisperSettings.HistoryRetentionDays} days for this Windows account."
                : "Session memory only. Closing Soltex clears it."));
    }

    private async void WhisperPanel_PrivacyRequested(
        object? sender,
        WhisperPrivacyRequestedEventArgs e)
    {
        if (_shutdownStarted)
        {
            return;
        }

        _whisperHistoryDrained = SaveWhisperPrivacyAsync(
            e,
            _whisperRuntimeCancellation.Token);
        await _whisperHistoryDrained;
    }

    private async Task SaveWhisperPrivacyAsync(
        WhisperPrivacyRequestedEventArgs e,
        CancellationToken cancellationToken)
    {
        bool gateEntered = false;
        bool stagedEncryptedState = false;
        try
        {
            await _whisperHistoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateEntered = true;
            WhisperSettingsDocument document = _whisperSettings.ToDocument();
            document.AutoSendEnabled = e.AutoSendEnabled;
            document.AutoSendWarningAccepted = e.AutoSendWarningAccepted;
            document.ContextReadsAllowed = e.ContextReadsAllowed;
            document.HistoryMode = e.HistoryMode.ToString();
            document.HistoryRetentionDays = e.HistoryRetentionDays;
            document.ClipboardBehavior = e.ClipboardBehavior.ToString();
            document.ShowTranscriptPreview = e.ShowTranscriptPreview;

            WhisperSettingsLoadResult loaded = WhisperSettingsMigrator.Load(document);
            if (!loaded.IsClean)
            {
                await Dispatcher.InvokeAsync(() => WhisperPanel.SetPrivacyError(
                    "That privacy change was not saved because it did not pass safe settings validation."));
                return;
            }

            WhisperHistoryMode previousMode = _whisperSettings.HistoryMode;
            bool retainedStateChanged =
                previousMode != loaded.Settings.HistoryMode ||
                _whisperSettings.HistoryRetentionDays != loaded.Settings.HistoryRetentionDays;
            if (_whisperHistoryStore is not null && retainedStateChanged)
            {
                if (loaded.Settings.HistoryMode == WhisperHistoryMode.EncryptedDisk)
                {
                    await _whisperHistoryStore.RewriteAsync(
                        _whisperHistory.CreateSnapshot(),
                        loaded.Settings.HistoryRetentionDays,
                        cancellationToken).ConfigureAwait(false);
                    stagedEncryptedState =
                        previousMode != WhisperHistoryMode.EncryptedDisk;
                }
                else
                {
                    await _whisperHistoryStore.ClearAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            _whisperSettingsStore?.Save(loaded.Settings);
            _whisperSettings = loaded.Settings;
            if (_whisperSettings.HistoryMode == WhisperHistoryMode.Off)
            {
                _whisperHistory.Clear();
            }

            await Dispatcher.InvokeAsync(() =>
            {
                WhisperPanel.SetPrivacy(
                    _whisperSettings,
                    "Privacy and delivery settings saved for this Windows account.");
                UpdateWhisperHistoryView();
                UpdateWhisperReadiness();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown owns cancellation and waits for this operation to drain.
        }
        catch (Exception exception) when (
            IsExpectedWhisperSettingsFailure(exception) ||
            IsExpectedWhisperHistoryFailure(exception))
        {
            if (stagedEncryptedState && _whisperHistoryStore is not null)
            {
                try
                {
                    await _whisperHistoryStore.ClearAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception rollbackException) when (
                    IsExpectedWhisperHistoryFailure(rollbackException))
                {
                    // The visible error remains conservative and asks the user to
                    // review retention rather than claiming the rollback succeeded.
                }
            }

            await Dispatcher.InvokeAsync(() => WhisperPanel.SetPrivacyError(
                "That privacy change could not be completed. Review the current history setting before dictating."));
        }
        finally
        {
            if (gateEntered)
            {
                _whisperHistoryGate.Release();
            }
        }
    }

    private async Task LoadWhisperHistoryAsync(CancellationToken cancellationToken)
    {
        if (_whisperSettings.HistoryMode != WhisperHistoryMode.EncryptedDisk ||
            _whisperHistoryStore is null)
        {
            return;
        }

        bool gateEntered = false;
        try
        {
            await _whisperHistoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateEntered = true;
            IReadOnlyList<WhisperHistoryEntry> entries =
                await _whisperHistoryStore.LoadAsync(
                    _whisperSettings.HistoryRetentionDays,
                    cancellationToken).ConfigureAwait(false);
            foreach (WhisperHistoryEntry entry in entries)
            {
                _whisperHistory.Add(entry);
            }

            await Dispatcher.InvokeAsync(() => UpdateWhisperHistoryView());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown owns cancellation and waits for this operation to drain.
        }
        catch (Exception exception) when (IsExpectedWhisperHistoryFailure(exception))
        {
            _whisperHistory.Clear();
            await Dispatcher.InvokeAsync(() => UpdateWhisperHistoryView(
                "Encrypted history could not be opened. Clear it or choose another history mode."));
        }
        finally
        {
            if (gateEntered)
            {
                _whisperHistoryGate.Release();
            }
        }
    }

    private async Task DeleteWhisperHistoryEntryAsync(
        WhisperHistoryEntry entry,
        CancellationToken cancellationToken)
    {
        bool gateEntered = false;
        try
        {
            await _whisperHistoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateEntered = true;
            WhisperHistoryEntry[] snapshot = _whisperHistory.CreateSnapshot().ToArray();
            int removalIndex = Array.FindIndex(snapshot, candidate => candidate == entry);
            if (removalIndex < 0)
            {
                return;
            }

            WhisperHistoryEntry[] desired = snapshot
                .Where((_, index) => index != removalIndex)
                .ToArray();
            if (_whisperSettings.HistoryMode == WhisperHistoryMode.EncryptedDisk &&
                _whisperHistoryStore is not null)
            {
                await _whisperHistoryStore.RewriteAsync(
                    desired,
                    _whisperSettings.HistoryRetentionDays,
                    cancellationToken).ConfigureAwait(false);
            }

            _ = _whisperHistory.Remove(entry);
            await Dispatcher.InvokeAsync(() => UpdateWhisperHistoryView(
                _whisperSettings.HistoryMode == WhisperHistoryMode.EncryptedDisk
                    ? "History entry deleted from encrypted retention."
                    : "History entry deleted from this session."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown owns cancellation and waits for this operation to drain.
        }
        catch (Exception exception) when (IsExpectedWhisperHistoryFailure(exception))
        {
            await Dispatcher.InvokeAsync(() => UpdateWhisperHistoryView(
                "That entry could not be deleted from encrypted retention."));
        }
        finally
        {
            if (gateEntered)
            {
                _whisperHistoryGate.Release();
            }
        }
    }

    private async Task ClearWhisperHistoryAsync(CancellationToken cancellationToken)
    {
        bool gateEntered = false;
        try
        {
            await _whisperHistoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateEntered = true;
            if (_whisperHistoryStore is not null)
            {
                await _whisperHistoryStore.ClearAsync(cancellationToken).ConfigureAwait(false);
            }

            _whisperHistory.Clear();
            await Dispatcher.InvokeAsync(() => UpdateWhisperHistoryView(
                "History cleared from memory and Soltex-owned retained state."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown owns cancellation and waits for this operation to drain.
        }
        catch (Exception exception) when (IsExpectedWhisperHistoryFailure(exception))
        {
            await Dispatcher.InvokeAsync(() => UpdateWhisperHistoryView(
                "History could not be cleared from encrypted retention."));
        }
        finally
        {
            if (gateEntered)
            {
                _whisperHistoryGate.Release();
            }
        }
    }

    private void WhisperPanel_LibraryRequested(
        object? sender,
        WhisperLibraryRequestedEventArgs e)
    {
        try
        {
            WhisperSettingsDocument document = _whisperSettings.ToDocument();
            document.Snippets = e.Snippets.Select(snippet => new WhisperSnippetDocument
            {
                Cue = snippet.Cue,
                Content = snippet.Content
            }).ToArray();
            document.CustomStyles = e.CustomStyles.Select(style =>
                new WhisperStyleProfileDocument
                {
                    Name = style.Name,
                    Kind = style.Kind.ToString(),
                    ProseCleanup = style.ProseCleanup,
                    SpokenPunctuation = style.SpokenPunctuation,
                    PreserveLiteralTokens = style.PreserveLiteralTokens,
                    CapitalizeSentences = style.CapitalizeSentences
                }).ToArray();
            document.ApplicationProfiles = e.ApplicationProfiles.Select(profile =>
                new WhisperAppProfileDocument
                {
                    ProcessName = profile.ProcessName,
                    AutoSendAllowed = profile.AutoSendAllowed,
                    TerminalAutoSendAllowed = profile.TerminalAutoSendAllowed,
                    ClipboardFallbackAllowed = profile.ClipboardFallbackAllowed,
                    ContextFormattingAllowed = profile.ContextFormattingAllowed,
                    StyleName = profile.StyleName
                }).ToArray();

            WhisperSettingsLoadResult loaded = WhisperSettingsMigrator.Load(document);
            if (!loaded.IsClean)
            {
                WhisperPanel.SetLibraryError(
                    "That rule was not saved because it did not pass safe settings validation.");
                return;
            }

            _whisperSettingsStore?.Save(loaded.Settings);
            _whisperSettings = loaded.Settings;
            WhisperPanel.SetLibrary(
                _whisperSettings,
                "Personal library saved locally for this Windows account.");
            WhisperPanel.SetPersonalization(
                _whisperSettings,
                "Personalization saved locally for this Windows account.");
            UpdateWhisperReadiness();
        }
        catch (Exception exception) when (IsExpectedWhisperSettingsFailure(exception))
        {
            WhisperPanel.SetLibraryError(
                "That rule was not saved. Check its name, size, and permissions.");
        }
    }

    private void WhisperPanel_PersonalizationRequested(
        object? sender,
        WhisperPersonalizationRequestedEventArgs e)
    {
        try
        {
            WhisperVocabulary vocabulary = new(e.VocabularyTerms);
            WhisperSettingsDocument document = _whisperSettings.ToDocument();
            document.PreferredLanguageTag = e.LanguageTag;
            document.DefaultStyleName = e.StyleName;
            document.VocabularyTerms = vocabulary.Terms.ToArray();
            WhisperSettings validated = WhisperSettingsMigrator.Load(document).Settings;
            _whisperSettingsStore?.Save(validated);
            _whisperSettings = validated;
            WhisperPanel.SetPersonalization(
                validated,
                "Personalization saved locally for this Windows account.");
        }
        catch (Exception exception) when (IsExpectedWhisperSettingsFailure(exception))
        {
            WhisperPanel.SetPersonalizationError(
                "That change was not saved. Use one printable term of at most 64 characters.");
        }
    }

    private void WhisperPanel_LocalProviderSelectRequested(object? sender, EventArgs e)
    {
        if (_shutdownStarted)
        {
            return;
        }

        try
        {
            WhisperSettingsDocument document = _whisperSettings.ToDocument();
            document.TranscriberId = WhisperLocalModelDefaults.ProviderId;
            document.TranscriptionModelId = WhisperLocalModelDefaults.ModelId;
            document.TranscriptionRuntimeId = WhisperLocalModelDefaults.RuntimeId;
            WhisperSettingsLoadResult loaded = WhisperSettingsMigrator.Load(document);
            if (!loaded.IsClean)
            {
                WhisperPanel.SetLocalModelStatus(
                    _whisperSettings,
                    _whisperModelStatus ?? CreateUncheckedWhisperModelStatus(),
                    operationRunning: false,
                    progress: 0,
                    "The local provider selection did not pass safe settings validation.");
                return;
            }

            _whisperSettingsStore?.Save(loaded.Settings);
            _whisperSettings = loaded.Settings;
            UpdateWhisperModelView(
                "Local transcription selected. Model bytes stay on this Windows account.");
            UpdateWhisperReadiness();
        }
        catch (Exception exception) when (IsExpectedWhisperSettingsFailure(exception))
        {
            UpdateWhisperModelView(
                "The local provider selection could not be saved for this Windows account.");
        }
    }

    private async void WhisperPanel_ModelActionRequested(
        object? sender,
        WhisperModelActionRequestedEventArgs e)
    {
        if (e.Action == WhisperModelRequestedAction.Cancel)
        {
            _whisperModelCancellation?.Cancel();
            return;
        }

        if (_shutdownStarted || !_whisperModelDrained.IsCompleted ||
            _whisperModelManager is null)
        {
            return;
        }

        _whisperModelCancellation?.Dispose();
        _whisperModelCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _whisperRuntimeCancellation.Token);
        _whisperModelDrained = RunWhisperModelOperationAsync(
            e.Action,
            _whisperModelCancellation.Token);
        await _whisperModelDrained;
    }

    private async void WhisperPanel_ModelDeleteRequested(object? sender, EventArgs e)
    {
        if (_shutdownStarted || !_whisperModelDrained.IsCompleted ||
            _whisperModelManager is null)
        {
            return;
        }

        MessageBoxResult choice = MessageBox.Show(
            this,
            "Delete the local Whisper model?\n\nDictation will stop working until the approximately 547 MiB model is installed and verified again. Personal settings and history are preserved.",
            "Delete local Whisper model",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        _whisperModelCancellation?.Dispose();
        _whisperModelCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _whisperRuntimeCancellation.Token);
        _whisperModelDrained = DeleteWhisperModelAsync(
            _whisperModelCancellation.Token);
        await _whisperModelDrained;
    }

    private async Task RefreshWhisperModelStatusAsync(CancellationToken cancellationToken)
    {
        if (_whisperModelManager is null)
        {
            return;
        }

        try
        {
            _whisperModelStatus = await _whisperModelManager
                .GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateWhisperModelView(DescribeWhisperModelStatus(_whisperModelStatus));
                UpdateWhisperReadiness();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown or an explicit user cancellation owns the transition.
        }
        catch (WhisperModelInstallException exception)
        {
            await Dispatcher.InvokeAsync(() => UpdateWhisperModelView(
                WhisperRedaction.Sanitize(exception.Message, 200)));
        }
    }

    private async Task RunWhisperModelOperationAsync(
        WhisperModelRequestedAction action,
        CancellationToken cancellationToken)
    {
        WindowsWhisperLocalModelManager manager = _whisperModelManager ??
            throw new InvalidOperationException("The local model manager is unavailable.");
        Progress<WhisperModelInstallProgress> progress = new(value =>
        {
            double receivedMiB = value.ReceivedBytes / (1024d * 1024d);
            double expectedMiB = value.ExpectedBytes / (1024d * 1024d);
            WhisperPanel.SetLocalModelStatus(
                _whisperSettings,
                _whisperModelStatus ?? CreateUncheckedWhisperModelStatus(),
                operationRunning: true,
                progress: value.Fraction,
                $"Downloading and verifying locally · {receivedMiB:F0} of {expectedMiB:F0} MiB.");
        });
        WhisperPanel.SetLocalModelStatus(
            _whisperSettings,
            _whisperModelStatus ?? CreateUncheckedWhisperModelStatus(),
            operationRunning: true,
            progress: 0,
            action == WhisperModelRequestedAction.Repair
                ? "Repairing the exact-owned local model. No transcript or audio is involved."
                : "Downloading the pinned local model. Closing or cancelling removes its partial file.");
        try
        {
            if (action == WhisperModelRequestedAction.Repair)
            {
                await PrepareWhisperModelMutationAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            _whisperModelStatus = action == WhisperModelRequestedAction.Repair
                ? await manager.RepairAsync(progress, cancellationToken).ConfigureAwait(false)
                : await manager.InstallAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _whisperModelStatus = await manager
                .GetStatusAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (WhisperModelInstallException)
        {
            _whisperModelStatus = await manager
                .GetStatusAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                WhisperPanel.SetLocalModelStatus(
                    _whisperSettings,
                    _whisperModelStatus ?? CreateUncheckedWhisperModelStatus(),
                    operationRunning: false,
                    progress: 0,
                    DescribeWhisperModelStatus(
                        _whisperModelStatus ?? CreateUncheckedWhisperModelStatus()));
                UpdateWhisperReadiness();
            });
        }
    }

    private async Task DeleteWhisperModelAsync(CancellationToken cancellationToken)
    {
        WindowsWhisperLocalModelManager manager = _whisperModelManager ??
            throw new InvalidOperationException("The local model manager is unavailable.");
        WhisperPanel.SetLocalModelStatus(
            _whisperSettings,
            _whisperModelStatus ?? CreateUncheckedWhisperModelStatus(),
            operationRunning: true,
            progress: 0,
            "Removing only the exact-owned local model and recognized partial files.");
        try
        {
            await PrepareWhisperModelMutationAsync(cancellationToken)
                .ConfigureAwait(false);
            await manager.DeleteAsync(cancellationToken).ConfigureAwait(false);
            _whisperModelStatus = await manager
                .GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user or shutdown cancelled before exact-owned deletion completed.
        }
        catch (WhisperModelInstallException)
        {
            _whisperModelStatus = await manager
                .GetStatusAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                WhisperPanel.SetLocalModelStatus(
                    _whisperSettings,
                    _whisperModelStatus ?? CreateUncheckedWhisperModelStatus(),
                    operationRunning: false,
                    progress: 0,
                    DescribeWhisperModelStatus(
                        _whisperModelStatus ?? CreateUncheckedWhisperModelStatus()));
                UpdateWhisperReadiness();
            });
        }
    }

    private async Task PrepareWhisperModelMutationAsync(
        CancellationToken cancellationToken)
    {
        _whisperSessionCancellation?.Cancel();
        _ = _whisperSessionRunner?.CancelActive();
        await _whisperSessionDrained.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_whisperTranscriber is not null)
        {
            await _whisperTranscriber.UnloadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void UpdateWhisperModelView(string detail) =>
        WhisperPanel.SetLocalModelStatus(
            _whisperSettings,
            _whisperModelStatus ?? CreateUncheckedWhisperModelStatus(),
            operationRunning: false,
            progress: 0,
            detail);

    private static string DescribeWhisperModelStatus(WhisperModelStatus status) =>
        status.State switch
        {
            WhisperModelInstallState.Ready =>
                "Verified local model ready. Transcription stays on this PC; no cloud endpoint or credential is used.",
            WhisperModelInstallState.Invalid =>
                status.FailureReason ?? "The local model failed verification and must be repaired.",
            WhisperModelInstallState.Faulted =>
                status.FailureReason ?? "The local model operation failed and can be retried safely.",
            WhisperModelInstallState.Installing =>
                "The local model operation is in progress.",
            _ =>
                "Not installed. Download begins only when you choose Install."
        };

    private static WhisperModelStatus CreateUncheckedWhisperModelStatus() => new(
        WhisperLocalModelDefaults.ProviderId,
        WhisperLocalModelDefaults.ModelId,
        WhisperLocalModelDefaults.RuntimeId,
        WhisperModelInstallState.NotInstalled,
        ExpectedBytes: 0,
        InstalledBytes: 0,
        WhisperModelFailureKind.None,
        FailureReason: null);

    private async void WhisperPanel_FeatureEnabledRequested(
        object? sender,
        WhisperFeatureEnabledRequestedEventArgs e)
    {
        if (!_whisperShortcutDrained.IsCompleted || _shutdownStarted)
        {
            return;
        }

        _whisperShortcutDrained = SetWhisperEnabledAsync(
            e.Enabled,
            _whisperRuntimeCancellation.Token);
        await _whisperShortcutDrained;
    }

    private async Task SetWhisperEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        WhisperPanel.SetFeatureState(
            enabled,
            updating: true,
            shortcutsRegistered: _whisperShortcutHost?.IsRegistered == true,
            enabled ? "Turning on the validated Windows shortcut runtime." : "Removing Whisper shortcuts.");
        try
        {
            WhisperSettingsDocument document = _whisperSettings.ToDocument();
            document.Enabled = enabled;
            WhisperSettings validated = WhisperSettingsMigrator.Load(document).Settings;
            _whisperSettingsStore?.Save(validated);
            _whisperSettings = validated;
            if (!enabled)
            {
                _whisperCaptureCancellation?.Cancel();
                _whisperCapture?.CompleteCurrentCapture();
            }

            await ReconcileWhisperShortcutsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested && _shutdownStarted)
        {
            // Shutdown owns cancellation and the final disposal pass.
        }
        catch (Exception exception) when (IsExpectedWhisperSettingsFailure(exception))
        {
            await Dispatcher.InvokeAsync(() =>
            {
                WhisperPanel.SetFeatureState(
                    _whisperSettings.Enabled,
                    updating: false,
                    shortcutsRegistered: _whisperShortcutHost?.IsRegistered == true,
                    "The Whisper runtime setting could not be saved.");
                UpdateWhisperReadiness();
            });
        }
    }

    private async Task ReconcileWhisperShortcutsAsync(
        CancellationToken cancellationToken)
    {
        await _whisperShortcutGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_renderSmokeMode || !_whisperSettings.Enabled)
            {
                await DisposeWhisperShortcutHostAsync().ConfigureAwait(false);
                _whisperShortcutError = null;
            }
            else if (_whisperShortcutHost?.IsRegistered != true)
            {
                await DisposeWhisperShortcutHostAsync().ConfigureAwait(false);
                WindowsWhisperShortcutHost candidate = new();
                candidate.Faulted += WhisperShortcutHost_Faulted;
                try
                {
                    await candidate.RegisterAsync(
                        WhisperShortcutSet.CreateDefault(),
                        HandleWhisperShortcutSignalAsync,
                        cancellationToken).ConfigureAwait(false);
                    _whisperShortcutHost = candidate;
                    _whisperShortcutError = null;
                }
                catch (WhisperShortcutRegistrationException exception)
                {
                    candidate.Faulted -= WhisperShortcutHost_Faulted;
                    await candidate.DisposeAsync().ConfigureAwait(false);
                    _whisperShortcutError = WhisperRedaction.Sanitize(
                        exception.Message,
                        160);
                }
            }
        }
        finally
        {
            _whisperShortcutGate.Release();
        }

        await Dispatcher.InvokeAsync(() =>
        {
            bool registered = _whisperShortcutHost?.IsRegistered == true;
            string detail = !_whisperSettings.Enabled
                ? "Whisper is off. No global shortcuts are registered."
                : _renderSmokeMode
                    ? "Shortcuts are skipped during controlled render evidence."
                    : registered
                        ? "Whisper is on and its validated shortcuts are registered."
                        : _whisperShortcutError ??
                          "Windows did not confirm shortcut registration.";
            WhisperPanel.SetFeatureState(
                _whisperSettings.Enabled,
                updating: false,
                registered,
                detail);
            UpdateWhisperReadiness();
        });
    }

    private ValueTask HandleWhisperShortcutSignalAsync(
        WhisperShortcutSignal signal,
        CancellationToken cancellationToken)
    {
        WhisperShortcutIntent? intent = _whisperShortcutGestures.Observe(signal);
        if (intent is null)
        {
            return ValueTask.CompletedTask;
        }

        WhisperOwnerAcceptanceSnapshot acceptance = _whisperOwnerAcceptance
            .ObserveShortcut(signal, intent.Value);

        Task dispatch = Dispatcher.InvokeAsync(
            () =>
            {
                RenderWhisperOwnerAcceptance(acceptance);
                HandleWhisperShortcutIntent(intent.Value);
            },
            System.Windows.Threading.DispatcherPriority.Normal,
            cancellationToken).Task;
        return new ValueTask(dispatch);
    }

    private void HandleWhisperShortcutIntent(WhisperShortcutIntent intent)
    {
        WhisperSessionHostCommand command = WhisperSessionIntentRouter.Route(
            intent,
            _whisperSessionRunner?.HasActiveSession == true,
            _whisperSessionMode);
        switch (command.Action)
        {
            case WhisperSessionHostAction.Cancel:
                _whisperSessionCancellation?.Cancel();
                _ = _whisperSessionRunner?.CancelActive();
                _whisperCaptureCancellation?.Cancel();
                return;
            case WhisperSessionHostAction.CompleteCapture:
                _ = _whisperSessionRunner?.CompleteCapture();
                return;
            case WhisperSessionHostAction.OpenScratchpad:
                WhisperPanel.ShowScratchpad();
                ShowPanel(WhisperPanel, WhisperNavButton);
                return;
            case WhisperSessionHostAction.StartSession when command.Mode is WhisperCaptureMode mode:
                BeginWhisperSession(mode, command.HandsFreeLocked);
                return;
            case WhisperSessionHostAction.LockHandsFree:
                _whisperHandsFreeLocked = true;
                if (_whisperSessionRunner is not null)
                {
                    RenderWhisperSessionFrame(_whisperSessionRunner.CreateSnapshot());
                }

                return;
            case WhisperSessionHostAction.UseLastTranscript
                when command.LastTranscriptIntent is WhisperShortcutIntent lastIntent:
                BeginWhisperLastTranscriptAction(lastIntent);
                return;
            default:
                ShowWhisperUnavailable("That Whisper shortcut is not available in this state.");
                return;
        }
    }

    private void BeginWhisperSession(
        WhisperCaptureMode mode,
        bool handsFreeLocked)
    {
        if (_shutdownStarted || _whisperSessionRunner is null ||
            !_whisperSessionDrained.IsCompleted)
        {
            return;
        }

        WhisperReadinessReport readiness = WhisperReadinessEvaluator.Evaluate(
            CreateWhisperReadinessInputs());
        if (!readiness.CanDictate)
        {
            ShowWhisperUnavailable(readiness.Summary);
            return;
        }

        _whisperSessionMode = mode;
        _whisperHandsFreeLocked = handsFreeLocked;
        _whisperSessionTargetProcess = null;
        _whisperSessionTargetKnown = false;
        _whisperSessionDeliveryKind = WhisperDeliveryKind.None;
        _whisperSessionCancellation?.Dispose();
        _whisperSessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _whisperRuntimeCancellation.Token);
        _whisperSessionDrained = RunWhisperSessionAsync(
            mode,
            _whisperSessionCancellation.Token);
    }

    private async Task RunWhisperSessionAsync(
        WhisperCaptureMode mode,
        CancellationToken cancellationToken)
    {
        WhisperSessionRunner runner = _whisperSessionRunner ??
            throw new InvalidOperationException("The Whisper session runner is unavailable.");
        using CancellationTokenSource overlayTicks =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task tickTask = RunWhisperOverlayTickAsync(overlayTicks.Token);
        try
        {
            WhisperSessionRunResult result = await runner.RunAsync(
                new WhisperSessionRunRequest(
                    mode,
                    _whisperSettings,
                    _whisperSettings.Snippets),
                cancellationToken).ConfigureAwait(false);
            await RetainCompletedWhisperSessionAsync(result, cancellationToken)
                .ConfigureAwait(false);
            WhisperOwnerAcceptanceSnapshot acceptance = _whisperOwnerAcceptance
                .ObserveSpokenInsertion(
                    result.Finalization.Pipeline.Text.Length > 0,
                    result.Delivery,
                    result.Submission?.Verification ?? WhisperInsertionVerification.Unavailable);
            await Dispatcher.InvokeAsync(() => RenderWhisperOwnerAcceptance(acceptance));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested ||
                                                 _shutdownStarted)
        {
            // The session runner publishes the content-free cancelled state.
        }
        catch (Exception exception) when (IsExpectedWhisperSessionFailure(exception))
        {
            WhisperOwnerAcceptanceSnapshot acceptance =
                _whisperOwnerAcceptance.ObserveSessionFailure();
            await Dispatcher.InvokeAsync(() => RenderWhisperOwnerAcceptance(acceptance));
            if (exception is WhisperLocalTranscriptionException localFailure &&
                localFailure.Kind is WhisperLocalTranscriptionFailureKind.ModelUnavailable or
                    WhisperLocalTranscriptionFailureKind.ModelBusy)
            {
                await RefreshWhisperModelStatusAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            overlayTicks.Cancel();
            try
            {
                await tickTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The session lifetime owns this presentation timer.
            }

            _whisperHandsFreeLocked = false;
            _whisperSessionMode = null;
        }
    }

    private async Task RunWhisperOverlayTickAsync(CancellationToken cancellationToken)
    {
        bool expiryRequested = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)
                .ConfigureAwait(false);
            WhisperSessionRunner? runner = _whisperSessionRunner;
            if (runner is null)
            {
                return;
            }

            WhisperSessionSnapshot snapshot = runner.CreateSnapshot();
            await Dispatcher.InvokeAsync(() => RenderWhisperSessionFrame(snapshot));
            if (!expiryRequested && _whisperSessionMode == WhisperCaptureMode.HandsFree &&
                snapshot.StartedAtUtc is DateTimeOffset started &&
                DateTimeOffset.UtcNow - started >= WhisperLimits.MaximumHandsFreeDuration)
            {
                expiryRequested = true;
                _ = runner.CompleteCapture();
            }
        }
    }

    private void WhisperSessionRunner_StateChanged(
        object? sender,
        WhisperSessionStateChangedEventArgs e)
    {
        if (_shutdownStarted)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_shutdownStarted)
            {
                return;
            }

            _whisperSessionMode = e.Mode;
            _whisperSessionTargetProcess = e.TargetProcessName;
            _whisperSessionTargetKnown = e.TargetKind != WhisperTargetKind.Unknown &&
                !string.IsNullOrWhiteSpace(e.TargetProcessName);
            _whisperSessionDeliveryKind = e.DeliveryKind;
            WhisperOwnerAcceptanceSnapshot acceptance = _whisperOwnerAcceptance
                .ObserveSessionState(
                    e.Session.State,
                    TimeSpan.FromMilliseconds(Environment.TickCount64));
            RenderWhisperOwnerAcceptance(acceptance);
            RenderWhisperSessionFrame(e.Session);
        });
    }

    private void RenderWhisperSessionFrame(WhisperSessionSnapshot snapshot)
    {
        if (_shutdownStarted)
        {
            return;
        }

        WhisperCaptureMode? mode = _whisperSessionMode ?? snapshot.Mode;
        TimeSpan elapsed = snapshot.StartedAtUtc is DateTimeOffset started
            ? DateTimeOffset.UtcNow - started
            : TimeSpan.Zero;
        WhisperDurationState durationState = mode == WhisperCaptureMode.HandsFree
            ? elapsed >= WhisperLimits.MaximumHandsFreeDuration
                ? WhisperDurationState.Expired
                : elapsed >= WhisperLimits.HandsFreeWarningAt
                    ? WhisperDurationState.Warning
                    : WhisperDurationState.Current
            : WhisperDurationState.Current;
        WhisperOverlayView frame = WhisperOverlayPresenter.Project(new WhisperOverlayInputs(
            snapshot,
            mode,
            _whisperSessionTargetProcess,
            _whisperSessionTargetKnown,
            _whisperHandsFreeLocked,
            elapsed,
            durationState,
            _whisperSessionDeliveryKind,
            snapshot.LastError));
        WhisperOverlayWindow overlay = EnsureWhisperOverlay();
        _whisperOverlayAction = frame.State switch
        {
            WhisperOverlayState.Listening or WhisperOverlayState.LockedHandsFree or
                WhisperOverlayState.Transcribing or WhisperOverlayState.Cleaning =>
                () => _whisperSessionRunner?.CancelActive(),
            WhisperOverlayState.CopiedFallback =>
                () => BeginWhisperLastTranscriptAction(
                    WhisperShortcutIntent.PasteLastTranscript),
            WhisperOverlayState.Error => () =>
            {
                overlay.Hide();
                ShowPanel(WhisperPanel, WhisperNavButton);
            },
            _ => null
        };
        overlay.Render(frame);
        overlay.Left = Left + ((Width - overlay.Width) / 2);
        overlay.Top = Top + Height - 140;
    }

    private async Task RetainCompletedWhisperSessionAsync(
        WhisperSessionRunResult result,
        CancellationToken cancellationToken)
    {
        if (_whisperSettings.HistoryMode == WhisperHistoryMode.Off ||
            result.Finalization.Pipeline.Text.Length == 0)
        {
            return;
        }

        WhisperHistoryEntry entry = new(
            DateTimeOffset.UtcNow,
            result.CapturedTarget?.Context.ProcessName ?? "unknown",
            result.PresentationDeliveryKind,
            result.Finalization.Pipeline.Text);
        bool gateEntered = false;
        try
        {
            await _whisperHistoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateEntered = true;
            WhisperHistoryEntry[] desired = _whisperHistory.CreateSnapshot()
                .Append(entry)
                .TakeLast(WhisperLimits.MaximumHistoryEntries)
                .ToArray();
            if (_whisperSettings.HistoryMode == WhisperHistoryMode.EncryptedDisk &&
                _whisperHistoryStore is not null)
            {
                await _whisperHistoryStore.RewriteAsync(
                    desired,
                    _whisperSettings.HistoryRetentionDays,
                    cancellationToken).ConfigureAwait(false);
            }

            _whisperHistory.Add(entry);
            await Dispatcher.InvokeAsync(() => UpdateWhisperHistoryView(
                _whisperSettings.HistoryMode == WhisperHistoryMode.EncryptedDisk
                    ? "Completed dictation retained in encrypted local history."
                    : "Completed dictation retained for this session only."));
        }
        catch (Exception exception) when (IsExpectedWhisperHistoryFailure(exception))
        {
            await Dispatcher.InvokeAsync(() => UpdateWhisperHistoryView(
                "Dictation completed, but its optional history record was not retained."));
        }
        finally
        {
            if (gateEntered)
            {
                _whisperHistoryGate.Release();
            }
        }
    }

    private WhisperOverlayWindow EnsureWhisperOverlay()
    {
        if (_whisperOverlay is not null)
        {
            return _whisperOverlay;
        }

        WhisperOverlayWindow overlay = new() { Owner = this };
        overlay.ActionRequested += (_, _) => _whisperOverlayAction?.Invoke();
        overlay.Closed += (_, _) =>
        {
            _whisperOverlay = null;
            _whisperOverlayAction = null;
        };
        _whisperOverlay = overlay;
        return overlay;
    }

    private void BeginWhisperLastTranscriptAction(WhisperShortcutIntent intent)
    {
        if (_shutdownStarted || !_whisperSessionDrained.IsCompleted ||
            _whisperSessionRunner?.LastCompletedTranscript is not { Length: > 0 } transcript ||
            _whisperTargetInspector is null || _whisperTextDelivery is null ||
            _whisperVerifiedSubmitter is null)
        {
            ShowWhisperUnavailable(
                "No completed transcript is available for that shortcut yet.");
            return;
        }

        _whisperSessionCancellation?.Dispose();
        _whisperSessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _whisperRuntimeCancellation.Token);
        _whisperSessionDrained = RunWhisperLastTranscriptActionAsync(
            intent,
            transcript,
            _whisperSessionCancellation.Token);
    }

    private async Task RunWhisperLastTranscriptActionAsync(
        WhisperShortcutIntent intent,
        string transcript,
        CancellationToken cancellationToken)
    {
        WindowsWhisperTargetInspector inspector = _whisperTargetInspector ??
            throw new InvalidOperationException("Whisper target inspection is unavailable.");
        WindowsWhisperTextDelivery deliveryAdapter = _whisperTextDelivery ??
            throw new InvalidOperationException("Whisper text delivery is unavailable.");
        WindowsWhisperVerifiedSubmitter submitter = _whisperVerifiedSubmitter ??
            throw new InvalidOperationException("Whisper verified submission is unavailable.");
        try
        {
            WhisperTargetSnapshot? target = intent == WhisperShortcutIntent.CopyLastTranscript
                ? null
                : await inspector.InspectAsync(cancellationToken).ConfigureAwait(false);
            WhisperPipelineResult pipeline = new WhisperTextPipeline().Process(
                transcript,
                snippets: null,
                new WhisperTextOptions(
                    SmartFormatting: false,
                    Backtrack: false,
                    ExpandSnippets: false,
                    DetectTerminalSubmit: false));
            WhisperAppProfile? profile = target is null
                ? null
                : _whisperSettings.ApplicationProfiles.FirstOrDefault(candidate =>
                    candidate.MatchesProcess(target.Context.ProcessName));
            WhisperDeliveryDecision decision = intent == WhisperShortcutIntent.CopyLastTranscript
                ? new WhisperDeliveryPolicy().Evaluate(
                    pipeline,
                    WhisperTargetContext.Unknown,
                    profile: null,
                    autoSendEnabled: false)
                : new WhisperDeliveryPolicy().Evaluate(
                    pipeline,
                    target?.Context ?? WhisperTargetContext.Unknown,
                    profile,
                    _whisperSettings.AutoSendEnabled,
                    dedicatedSubmitShortcut:
                        intent == WhisperShortcutIntent.SubmitLastTranscript);
            WhisperTextDeliveryResult delivery = await deliveryAdapter.DeliverAsync(
                new WhisperTextDeliveryRequest(decision, target),
                cancellationToken).ConfigureAwait(false);
            WhisperVerifiedSubmitResult? submission = null;
            if (intent == WhisperShortcutIntent.SubmitLastTranscript && target is not null &&
                decision.Kind is WhisperDeliveryKind.InsertAndSubmit or
                    WhisperDeliveryKind.SubmitOnly)
            {
                submission = await submitter.SubmitAsync(
                    new WhisperVerifiedSubmitRequest(
                        decision,
                        target,
                        delivery,
                        _whisperSettings.AutoSendWarningAccepted),
                    cancellationToken).ConfigureAwait(false);
            }

            WhisperDeliveryKind presentation = delivery.Copied
                ? WhisperDeliveryKind.CopyText
                : submission?.EnterDispatched == true
                    ? decision.Kind
                    : delivery.MutationDispatched
                        ? WhisperDeliveryKind.InsertText
                        : WhisperDeliveryKind.None;
            await Dispatcher.InvokeAsync(() =>
            {
                _whisperSessionMode = WhisperCaptureMode.PushToTalk;
                _whisperSessionTargetProcess = target?.Context.ProcessName;
                _whisperSessionTargetKnown = target?.Context.IsKnown == true;
                _whisperSessionDeliveryKind = presentation;
                RenderWhisperSessionFrame(new WhisperSessionSnapshot(
                    WhisperSessionState.Completed,
                    WhisperCaptureMode.PushToTalk,
                    DateTimeOffset.UtcNow,
                    LastError: null));
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The cancel shortcut remains reversible before the irreversible submit.
        }
        catch (Exception exception) when (IsExpectedWhisperSessionFailure(exception))
        {
            await Dispatcher.InvokeAsync(() => ShowWhisperUnavailable(
                "Whisper could not complete the requested transcript action."));
        }
    }

    private void ShowWhisperUnavailable(string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        if (_shutdownStarted)
        {
            return;
        }

        WhisperOverlayWindow overlay = EnsureWhisperOverlay();

        _whisperOverlayAction = () =>
        {
            overlay.Hide();
            ShowPanel(WhisperPanel, WhisperNavButton);
        };
        overlay.Render(WhisperOverlayPresenter.Project(new WhisperOverlayInputs(
            new WhisperSessionSnapshot(
                WhisperSessionState.Faulted,
                Mode: null,
                StartedAtUtc: null,
                LastError: detail),
            Mode: null,
            TargetProcessName: null,
            TargetIsKnown: false,
            HandsFreeLocked: false,
            TimeSpan.Zero,
            WhisperDurationState.Current,
            WhisperDeliveryKind.None,
            ErrorDetail: detail)));
        overlay.Left = Left + ((Width - overlay.Width) / 2);
        overlay.Top = Top + Height - 140;
    }

    private void WhisperShortcutHost_Faulted(
        object? sender,
        WhisperShortcutHostFaultEventArgs e)
    {
        _whisperShortcutError = WhisperRedaction.Sanitize(e.Detail, 160);
        _ = Dispatcher.BeginInvoke(() =>
        {
            WhisperPanel.SetFeatureState(
                _whisperSettings.Enabled,
                updating: false,
                shortcutsRegistered: false,
                _whisperShortcutError);
            UpdateWhisperReadiness();
        });
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
                WhisperOwnerAcceptanceSnapshot acceptance = _whisperOwnerAcceptance
                    .ObserveMicrophoneAvailability(IsConfiguredWhisperDeviceAvailable());
                string detail = devices.Devices.Count == 0
                    ? "Windows reported no available microphone."
                    : $"{devices.Devices.Count} input device(s) available.";
                WhisperPanel.UpdateCaptureDevices(
                    devices,
                    _whisperSettings.InputDeviceId,
                    detail);
                RenderWhisperOwnerAcceptance(acceptance);
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

        WhisperOwnerAcceptanceSnapshot acceptance = _whisperOwnerAcceptance
            .ObserveMicrophoneAvailability(available: false);
        _ = Dispatcher.BeginInvoke(() =>
        {
            RenderWhisperOwnerAcceptance(acceptance);
            WhisperPanel.SetMicrophoneTestState(
                running: true,
                $"The saved input is unavailable; using {selection.DeviceName} for this test.");
        });
    }

    private WhisperReadinessInputs CreateWhisperReadinessInputs()
    {
        bool selectedDeviceAvailable = _whisperSettings.InputDeviceId is { Length: > 0 } id &&
            _whisperDevices.Devices.Any(device =>
                string.Equals(device.Id, id, StringComparison.Ordinal));
        bool localTranscriberSelected = string.Equals(
                _whisperSettings.TranscriberId,
                WhisperLocalModelDefaults.ProviderId,
                StringComparison.Ordinal) &&
            string.Equals(
                _whisperSettings.TranscriptionModelId,
                WhisperLocalModelDefaults.ModelId,
                StringComparison.Ordinal) &&
            string.Equals(
                _whisperSettings.TranscriptionRuntimeId,
                WhisperLocalModelDefaults.RuntimeId,
                StringComparison.Ordinal);
        string? modelError = _whisperModelStatus?.State is
                WhisperModelInstallState.Invalid or WhisperModelInstallState.Faulted
            ? _whisperModelStatus.FailureReason ??
              "The local model failed verification and must be repaired."
            : null;
        return new WhisperReadinessInputs(
            FeatureEnabled: _whisperSettings.Enabled,
            MicrophoneSelected: selectedDeviceAvailable,
            MicrophonePermissionGranted: true,
            ShortcutsRegistered: _whisperShortcutHost?.IsRegistered == true,
            ShortcutRegistrationError: _whisperShortcutError,
            TranscriberConfigured: localTranscriberSelected,
            TranscriberCredentialAvailable: false,
            TargetInspectionAvailable: OperatingSystem.IsWindows(),
            AutoSendEnabled: _whisperSettings.AutoSendEnabled,
            AutoSendWarningAccepted: _whisperSettings.AutoSendWarningAccepted,
            EnabledAutoSendProfileCount: _whisperSettings.ApplicationProfiles.Count(
                profile => profile.AutoSendAllowed),
            TranscriberCredentialRequired: false,
            ModelAvailable: _whisperModelStatus?.IsVerified == true,
            TranscriberRuntimeAvailable:
                OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000),
            ModelInstallationError: modelError);
    }

    private void UpdateWhisperReadiness()
    {
        WhisperReadinessInputs inputs = CreateWhisperReadinessInputs();
        WhisperReadinessInputs captureInputs = WhisperCaptureReadiness.Apply(
            inputs,
            _whisperCaptureFailure,
            inputs.MicrophoneSelected);
        WhisperPanel.UpdateReadiness(captureInputs);
        WhisperPanel.SetOwnerAcceptance(
            _whisperOwnerAcceptance.CreateSnapshot(),
            WhisperReadinessEvaluator.Evaluate(captureInputs).CanDictate);
    }

    private bool IsConfiguredWhisperDeviceAvailable() =>
        _whisperSettings.InputDeviceId is { Length: > 0 } id &&
        _whisperDevices.Devices.Any(device =>
            string.Equals(device.Id, id, StringComparison.Ordinal));

    private void RenderWhisperOwnerAcceptance(WhisperOwnerAcceptanceSnapshot snapshot)
    {
        WhisperReadinessInputs inputs = WhisperCaptureReadiness.Apply(
            CreateWhisperReadinessInputs(),
            _whisperCaptureFailure,
            IsConfiguredWhisperDeviceAvailable());
        WhisperPanel.SetOwnerAcceptance(
            snapshot,
            WhisperReadinessEvaluator.Evaluate(inputs).CanDictate);
    }

    private async void WhisperPanel_OwnerAcceptanceRequested(
        object? sender,
        WhisperOwnerAcceptanceRequestedEventArgs e)
    {
        if (_shutdownStarted)
        {
            return;
        }

        switch (e.Action)
        {
            case WhisperOwnerAcceptanceAction.BeginNext:
                RenderWhisperOwnerAcceptance(_whisperOwnerAcceptance.BeginNext(
                    IsConfiguredWhisperDeviceAvailable()));
                break;
            case WhisperOwnerAcceptanceAction.RefreshMicrophones:
                await RefreshWhisperCaptureDevicesAsync();
                break;
            case WhisperOwnerAcceptanceAction.ConfirmAssistiveWalkthrough:
                RenderWhisperOwnerAcceptance(
                    _whisperOwnerAcceptance.ConfirmKeyboardAndScreenReader());
                break;
            case WhisperOwnerAcceptanceAction.Reset:
                RenderWhisperOwnerAcceptance(_whisperOwnerAcceptance.Reset());
                break;
            case WhisperOwnerAcceptanceAction.None:
            default:
                break;
        }
    }

    private void MainWindow_WhisperDpiChanged(object sender, DpiChangedEventArgs e) =>
        RenderWhisperOwnerAcceptance(_whisperOwnerAcceptance.ObserveDpiTransition(
            e.OldDpi.DpiScaleX,
            e.NewDpi.DpiScaleX));

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

    private async Task DisposeWhisperModelAsync()
    {
        _whisperModelCancellation?.Cancel();
        _whisperModelCancellation?.Dispose();
        _whisperModelCancellation = null;
        WindowsWhisperLocalModelManager? manager =
            Interlocked.Exchange(ref _whisperModelManager, null);
        if (manager is not null)
        {
            await manager.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task DisposeWhisperRuntimeAsync()
    {
        DpiChanged -= MainWindow_WhisperDpiChanged;
        WhisperPanel.OwnerAcceptanceRequested -= WhisperPanel_OwnerAcceptanceRequested;
        _whisperSessionCancellation?.Cancel();
        _whisperRuntimeCancellation.Cancel();
        await _whisperShortcutGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeWhisperShortcutHostAsync().ConfigureAwait(false);
        }
        finally
        {
            _whisperShortcutGate.Release();
            _whisperShortcutGate.Dispose();
            if (_whisperSessionRunner is not null)
            {
                _whisperSessionRunner.StateChanged -= WhisperSessionRunner_StateChanged;
                await _whisperSessionRunner.DisposeAsync().ConfigureAwait(false);
                _whisperSessionRunner = null;
            }

            if (_whisperTranscriber is not null)
            {
                await _whisperTranscriber.DisposeAsync().ConfigureAwait(false);
                _whisperTranscriber = null;
            }

            _whisperSessionCancellation?.Dispose();
            _whisperSessionCancellation = null;
            _whisperRuntimeCancellation.Dispose();
        }
    }

    private async Task DisposeWhisperHistoryAsync()
    {
        IWhisperHistoryRetentionStore? store =
            Interlocked.Exchange(ref _whisperHistoryStore, null);
        if (store is not null)
        {
            await store.DisposeAsync().ConfigureAwait(false);
        }

        _whisperHistoryGate.Dispose();
    }

    private async ValueTask DisposeWhisperShortcutHostAsync()
    {
        WindowsWhisperShortcutHost? host =
            Interlocked.Exchange(ref _whisperShortcutHost, null);
        if (host is null)
        {
            return;
        }

        host.Faulted -= WhisperShortcutHost_Faulted;
        await host.DisposeAsync().ConfigureAwait(false);
    }

    private static bool IsExpectedWhisperSettingsFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            InvalidOperationException or ArgumentException;

    private static bool IsExpectedWhisperHistoryFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            CryptographicException or Win32Exception or InvalidDataException or
            InvalidOperationException or ArgumentException;

    private static bool IsExpectedWhisperSessionFailure(Exception exception) =>
        exception is WhisperCaptureException or WhisperLocalTranscriptionException or
            WhisperClipboardUnavailableException or IOException or UnauthorizedAccessException or
            SecurityException or Win32Exception or InvalidDataException or
            InvalidOperationException or ArgumentException;
}
