using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Soltex.Monitoring;

internal static class NativeTelemetry
{
    internal static bool TryReadSystemTimes(out SystemTimesSample sample)
    {
        sample = default;
        if (!OperatingSystem.IsWindows() ||
            !NativeMethods.GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            return false;
        }

        sample = new SystemTimesSample(idle.Value, kernel.Value, user.Value);
        return true;
    }

    internal static bool TryReadMemory(out MemoryTelemetry? memory)
    {
        memory = null;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        MemoryStatusEx status = new()
        {
            Length = checked((uint)Marshal.SizeOf<MemoryStatusEx>())
        };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
        {
            return false;
        }

        memory = new MemoryTelemetry(
            status.TotalPhysical,
            Math.Min(status.AvailablePhysical, status.TotalPhysical),
            Math.Clamp(status.MemoryLoad, 0u, 100u),
            status.TotalPageFile,
            Math.Min(status.AvailablePageFile, status.TotalPageFile));
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        internal uint Low;
        internal uint High;

        internal readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(
            out FileTime idleTime,
            out FileTime kernelTime,
            out FileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }
}

internal readonly record struct SystemTimesSample(ulong Idle, ulong Kernel, ulong User);
