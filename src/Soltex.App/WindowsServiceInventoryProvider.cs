using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Soltex.App;

internal enum ServiceInventoryState
{
    Current,
    Partial,
    Unavailable
}

internal sealed record WindowsServiceObservation(
    string Name,
    string DisplayName,
    string Status,
    string StartMode,
    string Signal);

internal sealed class WindowsServiceInventorySnapshot
{
    internal WindowsServiceInventorySnapshot(
        DateTimeOffset capturedAtUtc,
        TimeSpan captureDuration,
        IReadOnlyList<WindowsServiceObservation> services,
        int observedServiceCount,
        int inaccessibleConfigurationCount,
        int omittedServiceCount,
        ServiceInventoryState state,
        string detail)
    {
        CapturedAtUtc = capturedAtUtc;
        CaptureDuration = captureDuration;
        Services = new ReadOnlyCollection<WindowsServiceObservation>(services.ToArray());
        ObservedServiceCount = Math.Max(0, observedServiceCount);
        InaccessibleConfigurationCount = Math.Max(0, inaccessibleConfigurationCount);
        OmittedServiceCount = Math.Max(0, omittedServiceCount);
        State = state;
        Detail = WindowsServiceInventoryProvider.SanitizeLabel(
            detail,
            WindowsServiceInventoryProvider.MaximumDetailLength,
            "No service detail was reported.");
    }

    internal DateTimeOffset CapturedAtUtc { get; }

    internal TimeSpan CaptureDuration { get; }

    internal IReadOnlyList<WindowsServiceObservation> Services { get; }

    internal int ObservedServiceCount { get; }

    internal int InaccessibleConfigurationCount { get; }

    internal int OmittedServiceCount { get; }

    internal ServiceInventoryState State { get; }

    internal string Detail { get; }

    internal string Provenance =>
        $"Windows Service Control Manager · {ObservedServiceCount} observed · read-only Win32 services · drivers excluded";
}

internal static class WindowsServiceInventoryProvider
{
    internal const int MaximumServiceCount = 512;
    internal const int MaximumObservedServiceCount = 2_048;
    internal const int MaximumNameLength = 128;
    internal const int MaximumDisplayNameLength = 160;
    internal const int MaximumDetailLength = 240;

    private const uint ScManagerEnumerateService = 0x0004;
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceWin32 = 0x0030;
    private const uint ServiceStateAll = 0x0003;
    private const int ScEnumProcessInfo = 0;
    private const int ErrorMoreData = 234;
    private const int ErrorInsufficientBuffer = 122;
    private const int MaximumEnumerationBufferBytes = 256 * 1_024;
    private const int MaximumConfigurationBufferBytes = 64 * 1_024;

    internal static Task<WindowsServiceInventorySnapshot> CaptureAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    internal static ServiceInventoryState ClassifyState(
        bool enumerationSucceeded,
        int inaccessibleConfigurationCount,
        int omittedServiceCount) =>
        !enumerationSucceeded
            ? ServiceInventoryState.Unavailable
            : inaccessibleConfigurationCount > 0 || omittedServiceCount > 0
                ? ServiceInventoryState.Partial
                : ServiceInventoryState.Current;

    internal static string ClassifySignal(
        uint serviceState,
        string startMode,
        uint win32ExitCode,
        uint serviceSpecificExitCode) =>
        serviceState switch
        {
            2 or 3 or 5 or 6 => "Changing",
            1 when string.Equals(startMode, "Automatic", StringComparison.Ordinal) &&
                   ((win32ExitCode != 0 && win32ExitCode != 1_077) ||
                    serviceSpecificExitCode != 0) => "Review",
            _ => string.Empty
        };

    internal static string SanitizeLabel(
        string? value,
        int maximumLength,
        string fallback)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string normalized = string.Join(
            ' ',
            value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => new string(part.Where(character => !char.IsControl(character)).ToArray())))
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized) || LooksPathLike(normalized))
        {
            return fallback;
        }

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static WindowsServiceInventorySnapshot Capture(CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!OperatingSystem.IsWindows())
        {
            stopwatch.Stop();
            return Unavailable(
                stopwatch.Elapsed,
                "Windows service inventory is available only on Windows.");
        }

        try
        {
            using SafeServiceHandle manager = OpenSCManagerW(
                null,
                null,
                ScManagerEnumerateService);
            if (manager.IsInvalid)
            {
                stopwatch.Stop();
                return Unavailable(
                    stopwatch.Elapsed,
                    "Windows did not allow read-only Service Control Manager enumeration.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            int bytesNeeded;
            int servicesReturned;
            int resumeHandle = 0;
            bool initialResult = EnumServicesStatusExW(
                manager,
                ScEnumProcessInfo,
                ServiceWin32,
                ServiceStateAll,
                IntPtr.Zero,
                0,
                out bytesNeeded,
                out servicesReturned,
                ref resumeHandle,
                null);
            int initialError = Marshal.GetLastWin32Error();
            if (initialResult || initialError != ErrorMoreData ||
                bytesNeeded <= 0 || bytesNeeded > MaximumEnumerationBufferBytes)
            {
                stopwatch.Stop();
                return Unavailable(
                    stopwatch.Elapsed,
                    "Windows did not return a bounded Win32 service inventory buffer.");
            }

            IntPtr buffer = Marshal.AllocHGlobal(bytesNeeded);
            try
            {
                resumeHandle = 0;
                bool complete = EnumServicesStatusExW(
                    manager,
                    ScEnumProcessInfo,
                    ServiceWin32,
                    ServiceStateAll,
                    buffer,
                    bytesNeeded,
                    out int remainingBytes,
                    out servicesReturned,
                    ref resumeHandle,
                    null);
                int enumerationError = Marshal.GetLastWin32Error();
                bool boundedPartial =
                    !complete && enumerationError == ErrorMoreData && servicesReturned > 0;
                if (!complete && !boundedPartial)
                {
                    stopwatch.Stop();
                    return Unavailable(
                        stopwatch.Elapsed,
                        "Windows service enumeration failed before a bounded result was available.");
                }

                int observedCount = Math.Min(
                    Math.Max(0, servicesReturned),
                    MaximumObservedServiceCount);
                int structureSize = Marshal.SizeOf<EnumServiceStatusProcess>();
                List<RawServiceObservation> raw = new(observedCount);
                for (int index = 0; index < observedCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnumServiceStatusProcess entry = Marshal.PtrToStructure<EnumServiceStatusProcess>(
                        IntPtr.Add(buffer, index * structureSize));
                    string serviceName = SanitizeLabel(
                        Marshal.PtrToStringUni(entry.ServiceName),
                        MaximumNameLength,
                        "Unnamed service");
                    string displayName = SanitizeLabel(
                        Marshal.PtrToStringUni(entry.DisplayName),
                        MaximumDisplayNameLength,
                        serviceName);
                    raw.Add(new RawServiceObservation(
                        serviceName,
                        displayName,
                        entry.Status.CurrentState,
                        entry.Status.Win32ExitCode,
                        entry.Status.ServiceSpecificExitCode));
                }

                RawServiceObservation[] selected = raw
                    .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumServiceCount)
                    .ToArray();
                int inaccessibleConfigurationCount = 0;
                List<WindowsServiceObservation> services = new(selected.Length);
                foreach (RawServiceObservation item in selected)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string startMode = QueryStartMode(
                        manager,
                        item.Name,
                        ref inaccessibleConfigurationCount);
                    services.Add(new WindowsServiceObservation(
                        item.Name,
                        item.DisplayName,
                        FormatStatus(item.State),
                        startMode,
                        ClassifySignal(
                            item.State,
                            startMode,
                            item.Win32ExitCode,
                            item.ServiceSpecificExitCode)));
                }

                int omittedCount = Math.Max(0, servicesReturned - selected.Length);
                if (boundedPartial || resumeHandle != 0 || remainingBytes > 0)
                {
                    omittedCount = Math.Max(1, omittedCount);
                }

                stopwatch.Stop();
                ServiceInventoryState state = ClassifyState(
                    enumerationSucceeded: true,
                    inaccessibleConfigurationCount,
                    omittedCount);
                string detail = state switch
                {
                    ServiceInventoryState.Current =>
                        "All returned Win32 service states and start modes were read.",
                    _ =>
                        "Some service configuration records were inaccessible or omitted; no values were inferred."
                };
                return new WindowsServiceInventorySnapshot(
                    DateTimeOffset.UtcNow,
                    stopwatch.Elapsed,
                    services,
                    observedCount,
                    inaccessibleConfigurationCount,
                    omittedCount,
                    state,
                    detail);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is DllNotFoundException or
                                           EntryPointNotFoundException or
                                           TypeLoadException or
                                           MarshalDirectiveException)
        {
            stopwatch.Stop();
            return Unavailable(
                stopwatch.Elapsed,
                "The documented Windows service boundary was unavailable.");
        }
    }

    private static string QueryStartMode(
        SafeServiceHandle manager,
        string serviceName,
        ref int inaccessibleConfigurationCount)
    {
        using SafeServiceHandle service = OpenServiceW(
            manager,
            serviceName,
            ServiceQueryConfig);
        if (service.IsInvalid)
        {
            inaccessibleConfigurationCount++;
            return "Not reported";
        }

        _ = QueryServiceConfigW(service, IntPtr.Zero, 0, out int bytesNeeded);
        int error = Marshal.GetLastWin32Error();
        if (error != ErrorInsufficientBuffer ||
            bytesNeeded <= 0 || bytesNeeded > MaximumConfigurationBufferBytes)
        {
            inaccessibleConfigurationCount++;
            return "Not reported";
        }

        IntPtr buffer = Marshal.AllocHGlobal(bytesNeeded);
        try
        {
            if (!QueryServiceConfigW(service, buffer, bytesNeeded, out _))
            {
                inaccessibleConfigurationCount++;
                return "Not reported";
            }

            QueryServiceConfig config = Marshal.PtrToStructure<QueryServiceConfig>(buffer);
            return config.StartType switch
            {
                0 => "Boot",
                1 => "System",
                2 => "Automatic",
                3 => "Manual",
                4 => "Disabled",
                _ => "Not reported"
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static WindowsServiceInventorySnapshot Unavailable(
        TimeSpan duration,
        string detail) =>
        new(
            DateTimeOffset.UtcNow,
            duration,
            Array.Empty<WindowsServiceObservation>(),
            observedServiceCount: 0,
            inaccessibleConfigurationCount: 0,
            omittedServiceCount: 0,
            ServiceInventoryState.Unavailable,
            detail);

    private static string FormatStatus(uint state) =>
        state switch
        {
            1 => "Stopped",
            2 => "Starting",
            3 => "Stopping",
            4 => "Running",
            5 => "Continuing",
            6 => "Pausing",
            7 => "Paused",
            _ => "Unknown"
        };

    private static bool LooksPathLike(string value) =>
        value.StartsWith("\\\\", StringComparison.Ordinal) ||
        value.StartsWith('@') ||
        value.Contains(":\\", StringComparison.Ordinal) ||
        value.Contains(":/", StringComparison.Ordinal) ||
        value.Contains('%');

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenSCManagerW(
        string? machineName,
        string? databaseName,
        uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusExW(
        SafeServiceHandle serviceControlManager,
        int infoLevel,
        uint serviceType,
        uint serviceState,
        IntPtr services,
        int bufferSize,
        out int bytesNeeded,
        out int servicesReturned,
        ref int resumeHandle,
        string? groupName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenServiceW(
        SafeServiceHandle serviceControlManager,
        string serviceName,
        uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfigW(
        SafeServiceHandle service,
        IntPtr serviceConfig,
        int bufferSize,
        out int bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceObject);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct EnumServiceStatusProcess
    {
        internal readonly IntPtr ServiceName;
        internal readonly IntPtr DisplayName;
        internal readonly ServiceStatusProcess Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ServiceStatusProcess
    {
        internal readonly uint ServiceType;
        internal readonly uint CurrentState;
        internal readonly uint ControlsAccepted;
        internal readonly uint Win32ExitCode;
        internal readonly uint ServiceSpecificExitCode;
        internal readonly uint CheckPoint;
        internal readonly uint WaitHint;
        internal readonly uint ProcessId;
        internal readonly uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct QueryServiceConfig
    {
        internal readonly uint ServiceType;
        internal readonly uint StartType;
        internal readonly uint ErrorControl;
        internal readonly IntPtr BinaryPathName;
        internal readonly IntPtr LoadOrderGroup;
        internal readonly uint TagId;
        internal readonly IntPtr Dependencies;
        internal readonly IntPtr ServiceStartName;
        internal readonly IntPtr DisplayName;
    }

    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeServiceHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    private readonly record struct RawServiceObservation(
        string Name,
        string DisplayName,
        uint State,
        uint Win32ExitCode,
        uint ServiceSpecificExitCode);
}
