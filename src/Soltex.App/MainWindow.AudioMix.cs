using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Soltex.Audio;

namespace Soltex.App;

public partial class MainWindow
{
    private AudioSessionSnapshot? _lastAudioSessionSnapshot;
    private CancellationTokenSource? _audioMixCancellation;
    private Task _audioMixOperationDrained = Task.CompletedTask;

    private async void MixerPanel_CaptureMixSnapshotRequested(object? sender, EventArgs e)
    {
        if (MixerPanel.MixSnapshot is not null)
        {
            MessageBoxResult choice = MessageBox.Show(
                this,
                "Replace the saved Audio mix snapshot with the current controllable app sessions?",
                "Replace Audio mix snapshot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (choice != MessageBoxResult.Yes)
            {
                return;
            }
        }

        MixerPanel.ShowMixSnapshotPending("Reading the current controllable app mix before saving it locally.");
        await RunAudioMixOperationAsync(async cancellationToken =>
        {
            AudioSessionSnapshot current =
                await AudioSessionProvider.CaptureAsync(cancellationToken);
            _lastAudioSessionSnapshot = current;
            AudioMixCaptureResult capture = AudioMixSnapshotPlanner.Capture(current.Sessions);
            if (capture.Snapshot is null)
            {
                MixerPanel.ShowMixSnapshotResult(
                    "NOT SAVED",
                    "WarningBrush",
                    "No unique controllable app session was available; the existing snapshot was left unchanged.");
                return;
            }

            _audioMixSnapshotStore.Save(capture.Snapshot);
            string omissions = DescribeMixOmissions(capture);
            MixerPanel.UpdateMixSnapshot(new AudioMixLoadResult(
                capture.Snapshot,
                RecoveredFromInvalid: false,
                AudioMixSnapshotStore.Describe(capture.Snapshot) + omissions));
            AddActivity(
                $"Audio mix snapshot captured for {capture.Snapshot.Entries.Count} app sessions.",
                "Audio");
        });
    }

    private async void MixerPanel_ApplyMixSnapshotRequested(object? sender, EventArgs e)
    {
        AudioMixSnapshot? snapshot = MixerPanel.MixSnapshot;
        if (snapshot is null)
        {
            return;
        }

        MixerPanel.ShowMixSnapshotPending(
            "Re-observing active sessions before applying the saved mix with Windows read-back.");
        await RunAudioMixOperationAsync(async cancellationToken =>
        {
            AudioSessionSnapshot current =
                await AudioSessionProvider.CaptureAsync(cancellationToken);
            _lastAudioSessionSnapshot = current;
            AudioMixApplyPlan plan = AudioMixSnapshotPlanner.Plan(snapshot, current.Sessions);
            int verified = 0;
            int failed = 0;
            foreach (AudioMixMatch match in plan.Matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AudioSessionMutationResult volume = await AudioSessionController.SetVolumeAsync(
                    match.Session,
                    match.Entry.VolumePercent,
                    cancellationToken);
                AudioSessionMutationResult mute = await AudioSessionController.SetMuteAsync(
                    match.Session,
                    match.Entry.IsMuted,
                    cancellationToken);
                if (volume.Succeeded && mute.Succeeded)
                {
                    verified++;
                }
                else
                {
                    failed++;
                }
            }

            await RefreshAudioAsync();
            int unresolved = failed + plan.MissingCount + plan.AmbiguousCount;
            string detail = DescribeMixApplyResult(
                snapshot.Entries.Count,
                verified,
                failed,
                plan.MissingCount,
                plan.AmbiguousCount);
            MixerPanel.ShowMixSnapshotResult(
                unresolved == 0 && verified == snapshot.Entries.Count
                    ? "VERIFIED"
                    : verified > 0 ? "PARTIAL" : "CHECK",
                unresolved == 0 && verified == snapshot.Entries.Count
                    ? "SignalBrush"
                    : verified > 0 ? "WarningBrush" : "DangerBrush",
                detail);
            AddActivity(detail, "Audio");
        });
    }

    private void MixerPanel_ClearMixSnapshotRequested(object? sender, EventArgs e)
    {
        MessageBoxResult choice = MessageBox.Show(
            this,
            "Clear the saved Audio mix snapshot from this Windows account? Current Windows audio levels will not change.",
            "Clear Audio mix snapshot",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _audioMixSnapshotStore.Clear();
            MixerPanel.UpdateMixSnapshot(new AudioMixLoadResult(
                null,
                RecoveredFromInvalid: false,
                "No mix snapshot has been captured."));
            AddActivity("Saved Audio mix snapshot cleared; Windows audio levels were not changed.", "Audio");
        }
        catch (Exception exception) when (IsExpectedAudioMixFailure(exception))
        {
            MixerPanel.ShowMixSnapshotResult(
                "CHECK",
                "DangerBrush",
                "The saved mix snapshot could not be cleared. Windows audio levels were not changed.");
        }
    }

    private async Task RunAudioMixOperationAsync(Func<CancellationToken, Task> action)
    {
        if (_audioMixCancellation is not null || _shutdownStarted)
        {
            return;
        }

        CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> drained = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _audioMixCancellation = cancellation;
        _audioMixOperationDrained = drained.Task;
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_shutdownStarted)
            {
                MixerPanel.ShowMixSnapshotResult(
                    "CANCELLED",
                    "WarningBrush",
                    "The Audio mix operation was cancelled. Any earlier verified writes remain visible in Windows.");
            }
        }
        catch (Exception exception) when (IsExpectedAudioMixFailure(exception))
        {
            if (!_shutdownStarted)
            {
                MixerPanel.ShowMixSnapshotResult(
                    "CHECK",
                    "DangerBrush",
                    "The Audio mix operation could not finish. Existing Windows audio state remains authoritative.");
            }
        }
        finally
        {
            if (ReferenceEquals(_audioMixCancellation, cancellation))
            {
                _audioMixCancellation = null;
            }

            cancellation.Dispose();
            drained.TrySetResult(true);
        }
    }

    private static bool IsExpectedAudioMixFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or InvalidDataException or
            InvalidOperationException or COMException or ExternalException;

    private static string DescribeMixOmissions(AudioMixCaptureResult capture)
    {
        List<string> details = [];
        if (capture.SkippedUncontrollable > 0)
        {
            details.Add($"{capture.SkippedUncontrollable} unavailable");
        }
        if (capture.SkippedAmbiguous > 0)
        {
            details.Add($"{capture.SkippedAmbiguous} ambiguous");
        }
        if (capture.OmittedByBound > 0)
        {
            details.Add($"{capture.OmittedByBound} beyond the bound");
        }

        return details.Count == 0
            ? string.Empty
            : " Not saved: " + string.Join(", ", details) + ".";
    }

    private static string DescribeMixApplyResult(
        int saved,
        int verified,
        int failed,
        int missing,
        int ambiguous)
    {
        string detail = $"Audio mix recall: {verified} of {saved} app sessions verified.";
        List<string> unresolved = [];
        if (failed > 0)
        {
            unresolved.Add($"{failed} write failures");
        }
        if (missing > 0)
        {
            unresolved.Add($"{missing} not active");
        }
        if (ambiguous > 0)
        {
            unresolved.Add($"{ambiguous} ambiguous");
        }

        return unresolved.Count == 0
            ? detail
            : detail + " Unresolved: " + string.Join(", ", unresolved) + ".";
    }
}
