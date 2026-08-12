using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace Soltex.App;

internal sealed record InstalledApplicationObservation(
    string Name,
    string Publisher,
    string Version,
    string Scope);

internal sealed record StartupApplicationObservation(
    string Name,
    string Scope,
    string Source,
    string Mode);

internal sealed class ApplicationInventorySnapshot
{
    internal ApplicationInventorySnapshot(
        DateTimeOffset capturedAtUtc,
        TimeSpan captureDuration,
        IReadOnlyList<InstalledApplicationObservation> installed,
        IReadOnlyList<StartupApplicationObservation> startup,
        int inaccessibleSourceCount)
    {
        CapturedAtUtc = capturedAtUtc;
        CaptureDuration = captureDuration;
        Installed = new ReadOnlyCollection<InstalledApplicationObservation>(installed.ToArray());
        Startup = new ReadOnlyCollection<StartupApplicationObservation>(startup.ToArray());
        InaccessibleSourceCount = Math.Max(0, inaccessibleSourceCount);
    }

    internal DateTimeOffset CapturedAtUtc { get; }

    internal TimeSpan CaptureDuration { get; }

    internal IReadOnlyList<InstalledApplicationObservation> Installed { get; }

    internal IReadOnlyList<StartupApplicationObservation> Startup { get; }

    internal int InaccessibleSourceCount { get; }

    internal string Provenance =>
        "Windows uninstall registry · Run/RunOnce registry · Startup folders";
}

internal static class ApplicationInventoryProvider
{
    internal const int MaximumInstalledApplicationCount = 2_048;
    internal const int MaximumStartupApplicationCount = 256;
    internal const int MaximumLabelLength = 120;
    private const int MaximumRegistrySubKeyScanPerSource = 4_096;
    private const int MaximumStartupValueScanPerSource = 512;
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string RunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOncePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

    internal static Task<ApplicationInventorySnapshot> CaptureAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    internal static string SanitizeLabel(string? value, string fallback = "Unknown")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        char[] clean = value
            .Trim()
            .Where(character => !char.IsControl(character))
            .Take(MaximumLabelLength)
            .ToArray();
        string result = new(clean);
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private static ApplicationInventorySnapshot Capture(CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<InstalledApplicationObservation> installed = [];
        List<StartupApplicationObservation> startup = [];
        HashSet<string> installedKeys = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> startupKeys = new(StringComparer.OrdinalIgnoreCase);
        int inaccessibleSourceCount = 0;

        foreach (RegistryInventorySource source in RegistrySources())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureInstalled(
                source,
                installed,
                installedKeys,
                ref inaccessibleSourceCount,
                cancellationToken);
            CaptureStartupRegistry(
                source,
                RunPath,
                "Registry Run",
                "Every sign-in",
                startup,
                startupKeys,
                ref inaccessibleSourceCount,
                cancellationToken);
            CaptureStartupRegistry(
                source,
                RunOncePath,
                "Registry RunOnce",
                "Next sign-in",
                startup,
                startupKeys,
                ref inaccessibleSourceCount,
                cancellationToken);
        }

        CaptureStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "Current user",
            startup,
            startupKeys,
            ref inaccessibleSourceCount,
            cancellationToken);
        CaptureStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            "All users",
            startup,
            startupKeys,
            ref inaccessibleSourceCount,
            cancellationToken);

        InstalledApplicationObservation[] orderedInstalled = installed
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Publisher, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumInstalledApplicationCount)
            .ToArray();
        StartupApplicationObservation[] orderedStartup = startup
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumStartupApplicationCount)
            .ToArray();
        stopwatch.Stop();
        return new ApplicationInventorySnapshot(
            DateTimeOffset.UtcNow,
            stopwatch.Elapsed,
            orderedInstalled,
            orderedStartup,
            inaccessibleSourceCount);
    }

    private static void CaptureInstalled(
        RegistryInventorySource source,
        List<InstalledApplicationObservation> destination,
        HashSet<string> seen,
        ref int inaccessibleSourceCount,
        CancellationToken cancellationToken)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(source.Hive, source.View);
            using RegistryKey? uninstall = baseKey.OpenSubKey(UninstallPath, writable: false);
            if (uninstall is null)
            {
                return;
            }

            foreach (string subKeyName in uninstall
                         .GetSubKeyNames()
                         .Take(MaximumRegistrySubKeyScanPerSource))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using RegistryKey? item = uninstall.OpenSubKey(subKeyName, writable: false);
                    if (item is null || IsHiddenInstallerEntry(item))
                    {
                        continue;
                    }

                    string name = SanitizeLabel(item.GetValue("DisplayName") as string, string.Empty);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string publisher = SanitizeLabel(
                        item.GetValue("Publisher") as string,
                        "Unknown publisher");
                    string version = SanitizeLabel(
                        item.GetValue("DisplayVersion") as string,
                        "Not reported");
                    string identity = string.Join(
                        '|',
                        name,
                        publisher,
                        version,
                        source.Scope);
                    if (seen.Add(identity))
                    {
                        destination.Add(new InstalledApplicationObservation(
                            name,
                            publisher,
                            version,
                            source.Scope));
                    }
                }
                catch (Exception exception) when (IsExpectedRegistryFailure(exception))
                {
                    inaccessibleSourceCount++;
                }
            }
        }
        catch (Exception exception) when (IsExpectedRegistryFailure(exception))
        {
            inaccessibleSourceCount++;
        }
    }

    private static bool IsHiddenInstallerEntry(RegistryKey item)
    {
        object? systemComponent = item.GetValue("SystemComponent");
        if (systemComponent is int systemFlag && systemFlag != 0 ||
            systemComponent is string systemText &&
            string.Equals(systemText, "1", StringComparison.Ordinal))
        {
            return true;
        }

        if (item.GetValue("ParentKeyName") is string)
        {
            return true;
        }

        string releaseType = item.GetValue("ReleaseType") as string ?? string.Empty;
        return releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
               releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
               releaseType.Contains("Security", StringComparison.OrdinalIgnoreCase);
    }

    private static void CaptureStartupRegistry(
        RegistryInventorySource source,
        string registryPath,
        string sourceLabel,
        string mode,
        List<StartupApplicationObservation> destination,
        HashSet<string> seen,
        ref int inaccessibleSourceCount,
        CancellationToken cancellationToken)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(source.Hive, source.View);
            using RegistryKey? run = baseKey.OpenSubKey(registryPath, writable: false);
            if (run is null)
            {
                return;
            }

            foreach (string valueName in run
                         .GetValueNames()
                         .Take(MaximumStartupValueScanPerSource))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddStartup(
                    SanitizeLabel(valueName, "Unnamed startup entry"),
                    source.Scope,
                    sourceLabel,
                    mode,
                    destination,
                    seen);
            }
        }
        catch (Exception exception) when (IsExpectedRegistryFailure(exception))
        {
            inaccessibleSourceCount++;
        }
    }

    private static void CaptureStartupFolder(
        string folderPath,
        string scope,
        List<StartupApplicationObservation> destination,
        HashSet<string> seen,
        ref int inaccessibleSourceCount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        try
        {
            foreach (string filePath in Directory
                         .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                         .Take(MaximumStartupValueScanPerSource))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddStartup(
                    SanitizeLabel(Path.GetFileNameWithoutExtension(filePath), "Unnamed startup item"),
                    scope,
                    "Startup folder",
                    "Every sign-in",
                    destination,
                    seen);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // An absent Startup folder is a valid empty source.
        }
        catch (Exception exception) when (exception is IOException or
                                           UnauthorizedAccessException or
                                           SecurityException)
        {
            inaccessibleSourceCount++;
        }
    }

    private static void AddStartup(
        string name,
        string scope,
        string source,
        string mode,
        List<StartupApplicationObservation> destination,
        HashSet<string> seen)
    {
        string identity = string.Join('|', name, scope, source, mode);
        if (seen.Add(identity))
        {
            destination.Add(new StartupApplicationObservation(name, scope, source, mode));
        }
    }

    private static bool IsExpectedRegistryFailure(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            SecurityException or
            PlatformNotSupportedException or
            ArgumentException;

    private static IEnumerable<RegistryInventorySource> RegistrySources()
    {
        yield return new RegistryInventorySource(
            RegistryHive.CurrentUser,
            RegistryView.Registry64,
            "Current user");
        yield return new RegistryInventorySource(
            RegistryHive.CurrentUser,
            RegistryView.Registry32,
            "Current user");
        yield return new RegistryInventorySource(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            "All users");
        yield return new RegistryInventorySource(
            RegistryHive.LocalMachine,
            RegistryView.Registry32,
            "All users");
    }

    private readonly record struct RegistryInventorySource(
        RegistryHive Hive,
        RegistryView View,
        string Scope);
}
