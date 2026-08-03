using System.Runtime.InteropServices;

namespace WaveSlate.Security;

public static class WindowsSecurityCenter
{
    private const uint AntivirusProvider = 0x4;

    public static WindowsSecurityHealth GetAntivirusHealth()
    {
        if (!OperatingSystem.IsWindows())
        {
            return WindowsSecurityHealth.Unknown;
        }

        try
        {
            int result = NativeMethods.WscGetSecurityProviderHealth(AntivirusProvider, out NativeHealth health);
            if (result < 0)
            {
                return WindowsSecurityHealth.Unknown;
            }

            return health switch
            {
                NativeHealth.Good => WindowsSecurityHealth.Good,
                NativeHealth.NotMonitored => WindowsSecurityHealth.NotMonitored,
                NativeHealth.Poor => WindowsSecurityHealth.Poor,
                NativeHealth.Snooze => WindowsSecurityHealth.Snoozed,
                _ => WindowsSecurityHealth.Unknown
            };
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return WindowsSecurityHealth.Unknown;
        }
    }

    private enum NativeHealth
    {
        Good = 0,
        NotMonitored = 1,
        Poor = 2,
        Snooze = 3
    }

    private static class NativeMethods
    {
        [DllImport("wscapi.dll")]
        internal static extern int WscGetSecurityProviderHealth(uint providers, out NativeHealth health);
    }
}
