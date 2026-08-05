using System.Runtime.InteropServices;

namespace Soltex.Security;

public sealed class WindowsSecurityChangeMonitor : IDisposable
{
    private readonly NativeMethods.ChangeCallback? _callback;
    private nint _registrationHandle;
    private int _disposed;

    public WindowsSecurityChangeMonitor()
    {
        if (!OperatingSystem.IsWindows())
        {
            Error = "Windows Security Center change notifications are only available on Windows.";
            return;
        }

        _callback = OnNativeChange;
        try
        {
            int result = NativeMethods.WscRegisterForChanges(
                nint.Zero,
                out _registrationHandle,
                _callback,
                nint.Zero);
            if (result < 0 || _registrationHandle == nint.Zero)
            {
                Error = $"Windows Security Center change registration failed with HRESULT 0x{result:X8}.";
                _registrationHandle = nint.Zero;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            Error = exception.Message;
            _registrationHandle = nint.Zero;
        }
    }

    public bool IsRegistered => _registrationHandle != nint.Zero && Volatile.Read(ref _disposed) == 0;

    public string? Error { get; }

    public event EventHandler? Changed;

    private uint OnNativeChange(nint context)
    {
        _ = context;
        if (Volatile.Read(ref _disposed) == 0)
        {
            ThreadPool.QueueUserWorkItem(
                static monitor => monitor.RaiseChanged(),
                this,
                preferLocal: false);
        }

        return 0;
    }

    private void RaiseChanged()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Delegate[] handlers = Changed?.GetInvocationList() ?? [];
        foreach (Delegate handler in handlers)
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                // A UI subscriber cannot terminate the native change-notification callback path.
            }
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        nint handle = Interlocked.Exchange(ref _registrationHandle, nint.Zero);
        if (handle != nint.Zero)
        {
            _ = NativeMethods.WscUnRegisterChanges(handle);
        }

        Changed = null;
        GC.SuppressFinalize(this);
    }

    private static class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate uint ChangeCallback(nint context);

        [DllImport("wscapi.dll")]
        internal static extern int WscRegisterForChanges(
            nint reserved,
            out nint callbackRegistration,
            ChangeCallback callbackAddress,
            nint context);

        [DllImport("wscapi.dll")]
        internal static extern int WscUnRegisterChanges(nint callbackRegistration);
    }
}
