using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Soltex.App;

internal sealed record RuntimeCostSample(
    string State,
    double WallMilliseconds,
    double ProcessCpuMilliseconds,
    double NormalizedCpuPercent,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    int HandleCount,
    int ThreadCount,
    int UiThreadId,
    double UiThreadCpuMilliseconds,
    int TopCpuThreadId,
    string TopCpuThreadRole,
    double TopCpuThreadCpuMilliseconds,
    bool WindowVisible,
    string WindowState,
    bool PerformanceSamplingActive);

internal sealed record RuntimeNavigationCost(
    int TransitionCount,
    double TotalMilliseconds,
    double MeanMilliseconds,
    double MaximumMilliseconds);

internal sealed class RuntimeCostReport
{
    internal RuntimeCostReport(
        string sourceHeadSha,
        string testedCommitSha,
        DateTimeOffset capturedAtUtc,
        double startupMilliseconds,
        IReadOnlyList<RuntimeCostSample> samples,
        RuntimeNavigationCost navigation)
    {
        SchemaVersion = 2;
        SourceHeadSha = RuntimeCostProbe.ValidateCommit(sourceHeadSha);
        TestedCommitSha = RuntimeCostProbe.ValidateCommit(testedCommitSha);
        CapturedAtUtc = capturedAtUtc;
        Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        ProcessorCount = Environment.ProcessorCount;
        StartupMilliseconds = Math.Max(0, startupMilliseconds);
        Samples = new ReadOnlyCollection<RuntimeCostSample>(samples.ToArray());
        Navigation = navigation;
    }

    public int SchemaVersion { get; }

    public string SourceHeadSha { get; }

    public string TestedCommitSha { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public string Framework { get; }

    public int ProcessorCount { get; }

    public double StartupMilliseconds { get; }

    public IReadOnlyList<RuntimeCostSample> Samples { get; }

    public RuntimeNavigationCost Navigation { get; }

    public IReadOnlyList<string> Limitations { get; } =
    [
        "Short owner-host or GitHub-runner samples are regression evidence, not a hardware benchmark.",
        "CPU is normalized across the logical processor count and includes the complete Soltex process.",
        "Hidden and minimized samples retain bounded Security/import observation but suspend Performance sampling."
    ];
}

internal static class RuntimeCostMath
{
    internal static double NormalizeCpuPercent(
        TimeSpan processCpu,
        TimeSpan wallTime,
        int processorCount)
    {
        if (processCpu < TimeSpan.Zero || wallTime <= TimeSpan.Zero || processorCount <= 0)
        {
            return 0;
        }

        double percent =
            processCpu.TotalMilliseconds /
            (wallTime.TotalMilliseconds * processorCount) * 100d;
        return Math.Clamp(percent, 0, 100);
    }
}

internal static class RuntimeCostProbe
{
    internal const int SampleDurationMilliseconds = 2_200;
    internal const int WarmupDurationMilliseconds = 750;
    internal const int PostNavigationSettleMilliseconds = 750;
    internal const int MinimizedSteadyDelayMilliseconds = 2_000;
    private const int TransitionSetCount = 2;

    internal static async Task<RuntimeCostReport> CaptureAsync(
        MainWindow window,
        string sourceHeadSha,
        string testedCommitSha,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        _ = ValidateCommit(sourceHeadSha);
        _ = ValidateCommit(testedCommitSha);
        using Process currentProcess = Process.GetCurrentProcess();
        int uiThreadId = IdentifyCurrentThreadIdByCpuCalibration(currentProcess);

        progress?.Invoke("startup-wait");
        Stopwatch startup = Stopwatch.StartNew();
        await window.StartupCompleted.WaitAsync(
            TimeSpan.FromSeconds(20),
            cancellationToken);
        startup.Stop();
        progress?.Invoke("startup-complete");
        progress?.Invoke("warmup-start");
        await Task.Delay(WarmupDurationMilliseconds, cancellationToken);
        progress?.Invoke("warmup-complete");
        window.TrySelectRenderSmokePanel("home");
        window.UpdateLayout();
        await WaitForPerformanceSamplingStateAsync(
            window,
            expected: true,
            cancellationToken);

        List<RuntimeCostSample> samples =
        [
            await MeasureIntervalAsync(
                window,
                "visible-idle",
                SampleDurationMilliseconds,
                uiThreadId,
                cancellationToken)
        ];
        progress?.Invoke("visible-idle-complete");

        string[] routes =
        [
            "home",
            "monitoring",
            "applications",
            "mixer",
            "security",
            "remote",
            "whisper",
            "activity",
            "update",
            "settings"
        ];
        List<double> transitionMilliseconds = new(routes.Length * TransitionSetCount);
        for (int pass = 0; pass < TransitionSetCount; pass++)
        {
            foreach (string route in routes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stopwatch transition = Stopwatch.StartNew();
                if (!window.TrySelectRenderSmokePanel(route))
                {
                    throw new InvalidOperationException(
                        $"Runtime probe route '{route}' was rejected.");
                }

                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Render);
                transition.Stop();
                transitionMilliseconds.Add(transition.Elapsed.TotalMilliseconds);
            }
        }

        RuntimeNavigationCost navigation = new(
            transitionMilliseconds.Count,
            transitionMilliseconds.Sum(),
            transitionMilliseconds.Average(),
            transitionMilliseconds.Max());
        progress?.Invoke("navigation-complete");

        window.TrySelectRenderSmokePanel("home");
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        await Task.Delay(PostNavigationSettleMilliseconds, cancellationToken);
        progress?.Invoke("navigation-settle-complete");

        window.WindowState = WindowState.Minimized;
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        await WaitForPerformanceSamplingStateAsync(
            window,
            expected: false,
            cancellationToken);
        samples.Add(await MeasureIntervalAsync(
            window,
            "minimized-transition",
            SampleDurationMilliseconds,
            uiThreadId,
            cancellationToken));
        progress?.Invoke("minimized-transition-complete");
        await Task.Delay(MinimizedSteadyDelayMilliseconds, cancellationToken);
        samples.Add(await MeasureIntervalAsync(
            window,
            "minimized-steady",
            SampleDurationMilliseconds,
            uiThreadId,
            cancellationToken));
        progress?.Invoke("minimized-steady-complete");

        window.WindowState = WindowState.Normal;
        window.Hide();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        await WaitForPerformanceSamplingStateAsync(
            window,
            expected: false,
            cancellationToken);
        await Task.Delay(750, cancellationToken);
        samples.Add(await MeasureIntervalAsync(
            window,
            "hidden-notification-area",
            SampleDurationMilliseconds,
            uiThreadId,
            cancellationToken));
        progress?.Invoke("hidden-complete");

        return new RuntimeCostReport(
            sourceHeadSha,
            testedCommitSha,
            DateTimeOffset.UtcNow,
            startup.Elapsed.TotalMilliseconds,
            samples,
            navigation);
    }

    internal static string ValidateCommit(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 40 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Runtime evidence commit identities must be full 40-character hexadecimal SHA-1 values.",
                nameof(value));
        }

        return normalized;
    }

    private static async Task<RuntimeCostSample> MeasureIntervalAsync(
        MainWindow window,
        string state,
        int durationMilliseconds,
        int uiThreadId,
        CancellationToken cancellationToken)
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        Dictionary<int, TimeSpan> startingThreadCpu = CaptureThreadCpu(process);
        TimeSpan startingCpu = process.TotalProcessorTime;
        Stopwatch wall = Stopwatch.StartNew();
        await Task.Delay(durationMilliseconds, cancellationToken);
        wall.Stop();
        process.Refresh();
        TimeSpan cpu = process.TotalProcessorTime - startingCpu;
        Dictionary<int, TimeSpan> endingThreadCpu = CaptureThreadCpu(process);
        IReadOnlyDictionary<int, TimeSpan> threadCpu = CalculateThreadCpu(
            startingThreadCpu,
            endingThreadCpu);
        KeyValuePair<int, TimeSpan> topThread = threadCpu
            .OrderByDescending(entry => entry.Value)
            .FirstOrDefault();
        double uiThreadCpuMilliseconds = threadCpu.TryGetValue(uiThreadId, out TimeSpan uiThreadCpu)
            ? uiThreadCpu.TotalMilliseconds
            : 0;
        return new RuntimeCostSample(
            state,
            wall.Elapsed.TotalMilliseconds,
            cpu.TotalMilliseconds,
            RuntimeCostMath.NormalizeCpuPercent(
                cpu,
                wall.Elapsed,
                Environment.ProcessorCount),
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.HandleCount,
            process.Threads.Count,
            uiThreadId,
            uiThreadCpuMilliseconds,
            topThread.Key,
            topThread.Key == uiThreadId ? "UI dispatcher" : "Worker or runtime",
            topThread.Value.TotalMilliseconds,
            window.IsVisible,
            window.WindowState.ToString(),
            window.IsPerformanceSamplingActive);
    }

    internal static IReadOnlyDictionary<int, TimeSpan> CalculateThreadCpu(
        IReadOnlyDictionary<int, TimeSpan> starting,
        IReadOnlyDictionary<int, TimeSpan> ending)
    {
        Dictionary<int, TimeSpan> result = [];
        foreach ((int threadId, TimeSpan endingCpu) in ending)
        {
            TimeSpan startingCpu = starting.TryGetValue(threadId, out TimeSpan observed)
                ? observed
                : TimeSpan.Zero;
            TimeSpan delta = endingCpu - startingCpu;
            result[threadId] = delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return result;
    }

    private static Dictionary<int, TimeSpan> CaptureThreadCpu(Process process)
    {
        Dictionary<int, TimeSpan> result = [];
        foreach (ProcessThread thread in process.Threads)
        {
            try
            {
                result[thread.Id] = thread.TotalProcessorTime;
            }
            catch (Exception exception) when (
                exception is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // A thread can exit between enumeration and observation.
            }
            finally
            {
                thread.Dispose();
            }
        }

        return result;
    }

    private static int IdentifyCurrentThreadIdByCpuCalibration(Process process)
    {
        Dictionary<int, TimeSpan> starting = CaptureThreadCpu(process);
        Stopwatch calibration = Stopwatch.StartNew();
        while (calibration.Elapsed < TimeSpan.FromMilliseconds(80))
        {
            Thread.SpinWait(2_048);
        }

        process.Refresh();
        Dictionary<int, TimeSpan> ending = CaptureThreadCpu(process);
        KeyValuePair<int, TimeSpan> observed = CalculateThreadCpu(starting, ending)
            .OrderByDescending(entry => entry.Value)
            .FirstOrDefault();
        if (observed.Key <= 0 || observed.Value < TimeSpan.FromMilliseconds(10))
        {
            throw new InvalidOperationException(
                "The runtime probe could not identify its dispatcher thread from process counters.");
        }

        return observed.Key;
    }

    private static async Task WaitForPerformanceSamplingStateAsync(
        MainWindow window,
        bool expected,
        CancellationToken cancellationToken)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (window.IsPerformanceSamplingActive == expected)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        }

        throw new InvalidOperationException(
            expected
                ? "Performance sampling did not start within three seconds."
                : "Performance sampling did not stop within three seconds.");
    }

}
