using System.Runtime.InteropServices;

namespace Soltex.Security;

public sealed class AmsiContentScanner : IContentScanner
{
    private const uint AmsiResultBlockedByAdminStart = 0x4000;
    private const uint AmsiResultBlockedByAdminEnd = 0x4fff;
    private const uint AmsiResultDetected = 0x8000;

    private nint _context;
    private bool _disposed;

    private AmsiContentScanner()
    {
        int result = NativeMethods.AmsiInitialize("Soltex.Security", out _context);
        Marshal.ThrowExceptionForHR(result);
    }

    public string EngineName => "Windows AMSI provider";

    public static IContentScanner CreateOrUnavailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new UnavailableContentScanner("AMSI is only available on Windows.");
        }

        try
        {
            return new AmsiContentScanner();
        }
        catch (Exception exception) when (exception is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            return new UnavailableContentScanner($"AMSI initialization failed: {exception.Message}");
        }
    }

    public ContentScanResult Scan(byte[] content, string contentName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentName);

        int result = NativeMethods.AmsiScanBuffer(
            _context,
            content,
            checked((uint)content.Length),
            contentName,
            nint.Zero,
            out uint nativeResult);

        if (result < 0)
        {
            return new ContentScanResult(
                ContentVerdict.Error,
                EngineName,
                nativeResult,
                $"AMSI scan failed with HRESULT 0x{result:X8}.");
        }

        if (nativeResult >= AmsiResultDetected)
        {
            return new ContentScanResult(
                ContentVerdict.Malware,
                EngineName,
                nativeResult,
                "The installed antimalware provider classified this content as malware.");
        }

        if (nativeResult is >= AmsiResultBlockedByAdminStart and <= AmsiResultBlockedByAdminEnd)
        {
            return new ContentScanResult(
                ContentVerdict.BlockedByPolicy,
                EngineName,
                nativeResult,
                "Windows policy blocked this content.");
        }

        return new ContentScanResult(
            ContentVerdict.Clean,
            EngineName,
            nativeResult,
            "The installed antimalware provider did not detect malicious content.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_context != nint.Zero)
        {
            NativeMethods.AmsiUninitialize(_context);
            _context = nint.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static class NativeMethods
    {
        [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
        internal static extern int AmsiInitialize(string appName, out nint amsiContext);

        [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
        internal static extern int AmsiScanBuffer(
            nint amsiContext,
            [In] byte[] buffer,
            uint length,
            string contentName,
            nint session,
            out uint result);

        [DllImport("amsi.dll")]
        internal static extern void AmsiUninitialize(nint amsiContext);
    }
}
