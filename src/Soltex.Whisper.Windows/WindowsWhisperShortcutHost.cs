using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

public sealed class WhisperShortcutRegistrationException : InvalidOperationException
{
    public WhisperShortcutRegistrationException(string message)
        : base(message)
    {
    }
}

public sealed class WhisperShortcutHostFaultEventArgs(string detail) : EventArgs
{
    public string Detail { get; } = detail;
}

public sealed class WindowsWhisperShortcutHost : IWhisperShortcutHost
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IWhisperShortcutRegistrationFactory _factory;
    private IWhisperShortcutRegistration? _registration;
    private int _disposeStarted;

    public WindowsWhisperShortcutHost()
        : this(new NativeWhisperShortcutRegistrationFactory())
    {
    }

    internal WindowsWhisperShortcutHost(IWhisperShortcutRegistrationFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public event EventHandler<WhisperShortcutHostFaultEventArgs>? Faulted;

    public bool IsRegistered => Volatile.Read(ref _registration)?.IsActive == true;

    public string? LastRegistrationError { get; private set; }

    public WhisperShortcutPerformanceSnapshot Performance =>
        Volatile.Read(ref _registration)?.Performance ??
        new WhisperShortcutPerformanceSnapshot(0, 0, 0, 0);

    public async ValueTask RegisterAsync(
        WhisperShortcutSet shortcutSet,
        Func<WhisperShortcutSignal, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shortcutSet);
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);

        WhisperShortcutValidationResult validation =
            WhisperShortcutRegistrationPolicy.Validate(shortcutSet);
        if (!validation.IsValid)
        {
            LastRegistrationError = validation.Error;
            throw new WhisperShortcutRegistrationException(
                validation.Error ?? "The shortcut set was rejected.");
        }

        _ = WindowsShortcutKeyMap.Compile(shortcutSet);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
            IWhisperShortcutRegistration? candidate = null;
            try
            {
                candidate = await _factory.CreateAsync(
                    shortcutSet,
                    handler,
                    ReportFault,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                IWhisperShortcutRegistration? previous =
                    Interlocked.Exchange(ref _registration, candidate);
                previous?.Deactivate();
                candidate.Activate();
                candidate = null;
                LastRegistrationError = null;
                if (previous is not null)
                {
                    await previous.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                if (candidate is not null)
                {
                    await candidate.DisposeAsync().ConfigureAwait(false);
                }

                throw;
            }
            catch (Exception exception) when (IsExpectedRegistrationFailure(exception))
            {
                if (candidate is not null)
                {
                    await candidate.DisposeAsync().ConfigureAwait(false);
                }

                string detail = exception is WhisperShortcutRegistrationException
                    ? exception.Message
                    : "Windows could not register the configured shortcuts.";
                LastRegistrationError = detail;
                throw new WhisperShortcutRegistrationException(detail);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            IWhisperShortcutRegistration? registration =
                Interlocked.Exchange(ref _registration, null);
            if (registration is not null)
            {
                await registration.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private void ReportFault(string detail)
    {
        string bounded = string.IsNullOrWhiteSpace(detail)
            ? "The Windows shortcut hook stopped unexpectedly."
            : detail.Trim();
        if (bounded.Length > 160)
        {
            bounded = bounded[..160];
        }

        LastRegistrationError = bounded;
        Faulted?.Invoke(this, new WhisperShortcutHostFaultEventArgs(bounded));
    }

    private static bool IsExpectedRegistrationFailure(Exception exception) =>
        exception is WhisperShortcutRegistrationException or Win32Exception or
            InvalidOperationException or NotSupportedException;
}

internal interface IWhisperShortcutRegistrationFactory
{
    ValueTask<IWhisperShortcutRegistration> CreateAsync(
        WhisperShortcutSet shortcutSet,
        Func<WhisperShortcutSignal, CancellationToken, ValueTask> handler,
        Action<string> faultHandler,
        CancellationToken cancellationToken);
}

internal interface IWhisperShortcutRegistration : IAsyncDisposable
{
    bool IsActive { get; }

    WhisperShortcutPerformanceSnapshot Performance { get; }

    void Activate();

    void Deactivate();
}

internal sealed class NativeWhisperShortcutRegistrationFactory :
    IWhisperShortcutRegistrationFactory
{
    public async ValueTask<IWhisperShortcutRegistration> CreateAsync(
        WhisperShortcutSet shortcutSet,
        Func<WhisperShortcutSignal, CancellationToken, ValueTask> handler,
        Action<string> faultHandler,
        CancellationToken cancellationToken)
    {
        NativeWhisperShortcutRegistration registration = new(
            shortcutSet,
            handler,
            faultHandler);
        try
        {
            await registration.StartAsync(cancellationToken).ConfigureAwait(false);
            return registration;
        }
        catch
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

internal sealed class NativeWhisperShortcutRegistration : IWhisperShortcutRegistration
{
    private const int QueueCapacity = 256;
    private const int KeyboardHookId = 13;
    private const int MouseHookId = 14;
    private const uint KeyDown = 0x0100;
    private const uint KeyUp = 0x0101;
    private const uint SystemKeyDown = 0x0104;
    private const uint SystemKeyUp = 0x0105;
    private const uint XButtonDown = 0x020B;
    private const uint XButtonUp = 0x020C;
    private const uint QuitMessage = 0x0012;
    private const uint NoRemove = 0x0000;
    private const uint KeyboardInjected = 0x00000010;
    private const uint MouseInjected = 0x00000001;

    private readonly WhisperShortcutMatcher _matcher;
    private readonly Func<WhisperShortcutSignal, CancellationToken, ValueTask> _handler;
    private readonly Action<string> _faultHandler;
    private readonly bool _needsMouseHook;
    private readonly Channel<WhisperShortcutSignal> _signals;
    private readonly CancellationTokenSource _dispatchCancellation = new();
    private readonly WhisperShortcutPerformanceRecorder _performance = new();
    private readonly TaskCompletionSource _started = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WindowsShortcutNative.HookProcedure _keyboardProcedure;
    private readonly WindowsShortcutNative.HookProcedure _mouseProcedure;
    private readonly Thread _hookThread;
    private readonly Task _dispatchTask;
    private nint _keyboardHook;
    private nint _mouseHook;
    private uint _hookThreadId;
    private int _active;
    private int _disposed;
    private int _queueFaulted;

    internal NativeWhisperShortcutRegistration(
        WhisperShortcutSet shortcutSet,
        Func<WhisperShortcutSignal, CancellationToken, ValueTask> handler,
        Action<string> faultHandler)
    {
        _matcher = new WhisperShortcutMatcher(shortcutSet);
        _handler = handler;
        _faultHandler = faultHandler;
        _needsMouseHook = shortcutSet.Bindings.Any(binding =>
            binding.Keys.Any(key => key is "Mouse 4" or "Mouse 5"));
        _signals = Channel.CreateBounded<WhisperShortcutSignal>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
        _keyboardProcedure = KeyboardCallback;
        _mouseProcedure = MouseCallback;
        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "Soltex Whisper shortcut hook"
        };
        _dispatchTask = DispatchAsync();
    }

    public WhisperShortcutPerformanceSnapshot Performance => _performance.CreateSnapshot();

    public bool IsActive => Volatile.Read(ref _active) != 0;

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        _hookThread.Start();
        await _started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Activate()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Volatile.Write(ref _active, 1);
    }

    public void Deactivate() => Volatile.Write(ref _active, 0);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _active, 0);
        uint threadId = Volatile.Read(ref _hookThreadId);
        if (threadId != 0)
        {
            _ = WindowsShortcutNative.PostThreadMessageW(threadId, QuitMessage, 0, 0);
        }

        try
        {
            await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _faultHandler("The Windows shortcut hook did not stop within two seconds.");
        }

        _signals.Writer.TryComplete();
        _dispatchCancellation.Cancel();
        try
        {
            await _dispatchTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Disposal owns dispatch cancellation.
        }
        finally
        {
            _dispatchCancellation.Dispose();
        }
    }

    private void HookThreadMain()
    {
        Volatile.Write(ref _hookThreadId, WindowsShortcutNative.GetCurrentThreadId());
        _ = WindowsShortcutNative.PeekMessageW(
            out _,
            0,
            0,
            0,
            NoRemove);
        try
        {
            nint module = WindowsShortcutNative.GetModuleHandleW(null);
            _keyboardHook = WindowsShortcutNative.SetWindowsHookExW(
                KeyboardHookId,
                _keyboardProcedure,
                module,
                0);
            if (_keyboardHook == 0)
            {
                throw CreateHookFailure("keyboard");
            }

            if (_needsMouseHook)
            {
                _mouseHook = WindowsShortcutNative.SetWindowsHookExW(
                    MouseHookId,
                    _mouseProcedure,
                    module,
                    0);
                if (_mouseHook == 0)
                {
                    throw CreateHookFailure("mouse");
                }
            }

            _started.TrySetResult();
            int messageResult;
            while ((messageResult = WindowsShortcutNative.GetMessageW(
                       out _,
                       0,
                       0,
                       0)) > 0)
            {
                // Low-level hooks dispatch through the message wait itself.
            }

            if (messageResult < 0 && Volatile.Read(ref _disposed) == 0)
            {
                _faultHandler("The Windows shortcut hook message loop stopped unexpectedly.");
            }
        }
        catch (Exception exception) when (
            exception is WhisperShortcutRegistrationException or Win32Exception)
        {
            _started.TrySetException(exception);
        }
        finally
        {
            Volatile.Write(ref _active, 0);
            if (_mouseHook != 0)
            {
                _ = WindowsShortcutNative.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = 0;
            }

            if (_keyboardHook != 0)
            {
                _ = WindowsShortcutNative.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = 0;
            }

            _signals.Writer.TryComplete();
            _started.TrySetException(new WhisperShortcutRegistrationException(
                "The Windows shortcut hook stopped before registration completed."));
            _stopped.TrySetResult();
        }
    }

    private nint KeyboardCallback(int code, nuint message, nint data)
    {
        long startedAt = Stopwatch.GetTimestamp();
        bool recordPerformance = false;
        try
        {
            if (code >= 0 && Volatile.Read(ref _active) != 0)
            {
                uint messageCode = checked((uint)message);
                bool? isDown = messageCode is KeyDown or SystemKeyDown
                    ? true
                    : messageCode is KeyUp or SystemKeyUp
                        ? false
                        : null;
                if (isDown is not null)
                {
                    WindowsShortcutNative.LowLevelKeyboardInput input =
                        Marshal.PtrToStructure<WindowsShortcutNative.LowLevelKeyboardInput>(data);
                    if (WindowsShortcutKeyMap.TryMapKeyboard(input.VirtualKey, out string key) &&
                        _matcher.IsRelevant(key))
                    {
                        recordPerformance = true;
                        if ((input.Flags & KeyboardInjected) == 0)
                        {
                            Publish(_matcher.Observe(
                                checked((int)input.VirtualKey),
                                key,
                                isDown.Value,
                                TimeSpan.FromMilliseconds(Environment.TickCount64)));
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            FailQueue();
        }

        if (recordPerformance)
        {
            _performance.Record(Stopwatch.GetTimestamp() - startedAt);
        }

        return WindowsShortcutNative.CallNextHookEx(0, code, message, data);
    }

    private nint MouseCallback(int code, nuint message, nint data)
    {
        long startedAt = Stopwatch.GetTimestamp();
        bool recordPerformance = false;
        try
        {
            if (code >= 0 && Volatile.Read(ref _active) != 0)
            {
                uint messageCode = checked((uint)message);
                if (messageCode is XButtonDown or XButtonUp)
                {
                    WindowsShortcutNative.LowLevelMouseInput input =
                        Marshal.PtrToStructure<WindowsShortcutNative.LowLevelMouseInput>(data);
                    int button = checked((int)((input.MouseData >> 16) & 0xFFFF));
                    string? key = button switch
                    {
                        1 => "Mouse 4",
                        2 => "Mouse 5",
                        _ => null
                    };
                    if (key is not null && _matcher.IsRelevant(key))
                    {
                        recordPerformance = true;
                        if ((input.Flags & MouseInjected) == 0)
                        {
                            Publish(_matcher.Observe(
                                button == 1
                                    ? WindowsShortcutKeyMap.Mouse4InputId
                                    : WindowsShortcutKeyMap.Mouse5InputId,
                                key,
                                messageCode == XButtonDown,
                                TimeSpan.FromMilliseconds(Environment.TickCount64)));
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            FailQueue();
        }

        if (recordPerformance)
        {
            _performance.Record(Stopwatch.GetTimestamp() - startedAt);
        }

        return WindowsShortcutNative.CallNextHookEx(0, code, message, data);
    }

    private void Publish(IReadOnlyList<WhisperShortcutSignal> signals)
    {
        foreach (WhisperShortcutSignal signal in signals)
        {
            if (!_signals.Writer.TryWrite(signal))
            {
                FailQueue();
                return;
            }
        }
    }

    private void FailQueue()
    {
        if (Interlocked.Exchange(ref _queueFaulted, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _active, 0);
        _signals.Writer.TryComplete();
    }

    private async Task DispatchAsync()
    {
        try
        {
            await foreach (WhisperShortcutSignal signal in _signals.Reader.ReadAllAsync(
                               _dispatchCancellation.Token).ConfigureAwait(false))
            {
                await _handler(signal, _dispatchCancellation.Token).ConfigureAwait(false);
            }

            if (Volatile.Read(ref _queueFaulted) != 0)
            {
                _faultHandler(
                    "The Windows shortcut queue stopped safely; re-register shortcuts before dictating.");
            }
        }
        catch (OperationCanceledException) when (_dispatchCancellation.IsCancellationRequested)
        {
            // Disposal owns dispatch cancellation.
        }
        catch (Exception)
        {
            Volatile.Write(ref _active, 0);
            _faultHandler(
                "A Whisper shortcut action failed safely; re-register shortcuts before dictating.");
            uint threadId = Volatile.Read(ref _hookThreadId);
            if (threadId != 0)
            {
                _ = WindowsShortcutNative.PostThreadMessageW(threadId, QuitMessage, 0, 0);
            }
        }
    }

    private static WhisperShortcutRegistrationException CreateHookFailure(string hookKind)
    {
        int error = Marshal.GetLastPInvokeError();
        return new WhisperShortcutRegistrationException(
            $"Windows could not register the {hookKind} shortcut hook (error {error}).");
    }
}
