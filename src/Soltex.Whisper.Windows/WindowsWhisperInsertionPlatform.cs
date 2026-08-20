using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using FormsClipboard = System.Windows.Forms.Clipboard;
using FormsDataObject = System.Windows.Forms.DataObject;
using FormsIDataObject = System.Windows.Forms.IDataObject;
using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

internal sealed class WindowsWhisperInsertionPlatform : IWindowsWhisperInsertionPlatform
{
    private readonly WindowsBoundedMtaOperationHost _uiaOperations = new(
        WindowsWhisperTargetInspector.DefaultInspectionTimeout);

    public async ValueTask<bool> TryInsertDirectAsync(
        WhisperTargetSnapshot capturedTarget,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capturedTarget);
        ArgumentNullException.ThrowIfNull(text);
        WindowsBoundedOperationResult<bool> operation = await _uiaOperations.RunAsync(
            () => TryInsertDirect(capturedTarget, text),
            cancellationToken).ConfigureAwait(false);
        return operation.Succeeded && operation.Value;
    }

    public ValueTask<IWindowsWhisperClipboardLease> StageClipboardAsync(
        string text,
        bool capturePrevious,
        CancellationToken cancellationToken) =>
        WindowsWhisperClipboardLease.StageAsync(text, capturePrevious, cancellationToken);

    public async ValueTask CopyAsync(string text, CancellationToken cancellationToken)
    {
        await using IWindowsWhisperClipboardLease clipboard =
            await StageClipboardAsync(
                text,
                capturePrevious: false,
                cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<bool> PasteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PasteNative.ModifierIsDown())
        {
            return ValueTask.FromResult(false);
        }

        PasteNative.Input[] inputs =
        [
            PasteNative.Input.Keyboard(PasteNative.ControlKey, keyUp: false),
            PasteNative.Input.Keyboard(PasteNative.VKey, keyUp: false),
            PasteNative.Input.Keyboard(PasteNative.VKey, keyUp: true),
            PasteNative.Input.Keyboard(PasteNative.ControlKey, keyUp: true)
        ];
        uint sent = PasteNative.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<PasteNative.Input>());
        if (sent != inputs.Length)
        {
            PasteNative.ReleaseControl();
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(true);
    }

    private static bool TryInsertDirect(WhisperTargetSnapshot capturedTarget, string text)
    {
        AutomationElement? element = AutomationElement.FocusedElement;
        if (element is null ||
            !WindowsWhisperAutomationIdentity.Matches(
                element,
                capturedTarget.Identity))
        {
            return false;
        }

        AutomationElement.AutomationElementInformation current = element.Current;
        if (!current.IsEnabled || !current.IsKeyboardFocusable || current.IsPassword)
        {
            return false;
        }

        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valueObject) ||
            valueObject is not ValuePattern valuePattern ||
            valuePattern.Current.IsReadOnly ||
            !element.TryGetCurrentPattern(TextPattern.Pattern, out object? textObject) ||
            textObject is not TextPattern textPattern)
        {
            return false;
        }

        TextPatternRange[] selections = textPattern.GetSelection();
        if (selections.Length != 1)
        {
            return false;
        }

        TextPatternRange selection = selections[0];
        TextPatternRange document = textPattern.DocumentRange;
        bool wholeValueSelected = selection.CompareEndpoints(
                TextPatternRangeEndpoint.Start,
                document,
                TextPatternRangeEndpoint.Start) == 0 &&
            selection.CompareEndpoints(
                TextPatternRangeEndpoint.End,
                document,
                TextPatternRangeEndpoint.End) == 0;
        if (!wholeValueSelected ||
            !WindowsWhisperAutomationIdentity.Matches(
                element,
                capturedTarget.Identity))
        {
            return false;
        }

        valuePattern.SetValue(text);
        AutomationElement? confirmation = AutomationElement.FocusedElement;
        return confirmation is not null &&
            WindowsWhisperAutomationIdentity.Matches(
                confirmation,
                capturedTarget.Identity);
    }
}

internal sealed class WindowsWhisperClipboardLease : IWindowsWhisperClipboardLease
{
    private const string OperationFormat = "Soltex.Whisper.Operation.v1";
    private static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(1);

    private readonly string _text;
    private readonly string _operationToken = Guid.NewGuid().ToString("N");
    private readonly bool _capturePrevious;
    private readonly BlockingCollection<Action> _commands = new(
        new ConcurrentQueue<Action>(),
        boundedCapacity: 8);
    private readonly TaskCompletionSource _staged = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private ComIDataObject? _previousClipboard;
    private bool _previousClipboardCaptured;
    private uint _ownedSequence;
    private int _disposed;

    private WindowsWhisperClipboardLease(string text, bool capturePrevious)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > WhisperLimits.MaximumTranscriptCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Clipboard text cannot exceed {WhisperLimits.MaximumTranscriptCharacters} characters.");
        }

        _text = text;
        _capturePrevious = capturePrevious;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "Soltex Whisper clipboard"
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    internal static async ValueTask<IWindowsWhisperClipboardLease> StageAsync(
        string text,
        bool capturePrevious,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WindowsWhisperClipboardLease lease = new(text, capturePrevious);
        lease._thread.Start();
        try
        {
            await lease._staged.Task
                .WaitAsync(StageTimeout, cancellationToken)
                .ConfigureAwait(false);
            return lease;
        }
        catch (OperationCanceledException)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw new WhisperClipboardUnavailableException(exception);
        }
    }

    public async ValueTask<WhisperClipboardRestoreOutcome> TryRestoreAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0)
        {
            return WhisperClipboardRestoreOutcome.Unavailable;
        }

        TaskCompletionSource<WhisperClipboardRestoreOutcome> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _commands.Add(
                () =>
                {
                    try
                    {
                        completion.TrySetResult(RestoreOnClipboardThread());
                    }
                    catch (Exception)
                    {
                        completion.TrySetResult(WhisperClipboardRestoreOutcome.Unavailable);
                    }
                },
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return WhisperClipboardRestoreOutcome.Unavailable;
        }

        try
        {
            return await completion.Task
                .WaitAsync(StageTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return WhisperClipboardRestoreOutcome.Unavailable;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool workerStopped = false;
        if (_commands.TryAdd(() =>
        {
            try
            {
                ReleasePreviousClipboard();
            }
            finally
            {
                completion.TrySetResult();
                _commands.CompleteAdding();
            }
        }))
        {
            try
            {
                await completion.Task.WaitAsync(StageTimeout).ConfigureAwait(false);
                workerStopped = _thread.Join(millisecondsTimeout: 100);
            }
            catch (TimeoutException)
            {
                // The worker is background-only. Shutdown must not wait forever on
                // a hostile clipboard owner.
            }
        }

        if (workerStopped || !_thread.IsAlive)
        {
            _commands.Dispose();
        }
    }

    private void ThreadMain()
    {
        int initializeResult = ClipboardNative.OleInitialize(IntPtr.Zero);
        bool uninitialize = initializeResult >= 0;
        try
        {
            StageOnClipboardThread();
            _staged.TrySetResult();
            foreach (Action command in _commands.GetConsumingEnumerable())
            {
                command();
            }
        }
        catch (Exception exception)
        {
            _staged.TrySetException(exception);
        }
        finally
        {
            ReleasePreviousClipboard();
            if (uninitialize)
            {
                ClipboardNative.OleUninitialize();
            }
        }
    }

    private void StageOnClipboardThread()
    {
        if (_capturePrevious)
        {
            int previousResult = ClipboardNative.GetClipboardWithRetry(
                out _previousClipboard);
            _previousClipboardCaptured = previousResult >= 0;
        }

        FormsDataObject data = new();
        data.SetData(System.Windows.Forms.DataFormats.UnicodeText, autoConvert: false, _text);
        data.SetData(OperationFormat, autoConvert: false, _operationToken);
        FormsClipboard.SetDataObject(data, copy: true, retryTimes: 5, retryDelay: 20);
        FormsIDataObject? staged = FormsClipboard.GetDataObject();
        _ = staged?.GetData(
            System.Windows.Forms.DataFormats.UnicodeText,
            autoConvert: false);
        string? stagedToken = staged?.GetData(
            OperationFormat,
            autoConvert: false) as string;
        if (!string.Equals(stagedToken, _operationToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Windows did not retain the clipboard ownership token.");
        }

        _ownedSequence = ClipboardNative.GetClipboardSequenceNumber();
        if (_ownedSequence == 0)
        {
            throw new Win32Exception("Windows did not expose a clipboard sequence number.");
        }
    }

    private WhisperClipboardRestoreOutcome RestoreOnClipboardThread()
    {
        if (!_previousClipboardCaptured)
        {
            return WhisperClipboardRestoreOutcome.Unavailable;
        }

        if (ClipboardNative.GetClipboardSequenceNumber() != _ownedSequence)
        {
            return WhisperClipboardRestoreOutcome.SkippedOwnershipChanged;
        }

        FormsIDataObject? current = FormsClipboard.GetDataObject();
        string? token = current?.GetDataPresent(OperationFormat, autoConvert: false) == true
            ? current.GetData(OperationFormat, autoConvert: false) as string
            : null;
        if (!string.Equals(token, _operationToken, StringComparison.Ordinal))
        {
            return WhisperClipboardRestoreOutcome.SkippedOwnershipChanged;
        }

        int result = ClipboardNative.SetClipboardWithRetry(_previousClipboard);
        return result >= 0
            ? WhisperClipboardRestoreOutcome.Restored
            : WhisperClipboardRestoreOutcome.Unavailable;
    }

    private void ReleasePreviousClipboard()
    {
        ComIDataObject? previous = Interlocked.Exchange(ref _previousClipboard, null);
        if (previous is not null && Marshal.IsComObject(previous))
        {
            _ = Marshal.FinalReleaseComObject(previous);
        }
    }
}

internal static class ClipboardNative
{
    internal static int GetClipboardWithRetry(out ComIDataObject? dataObject)
    {
        int result = 0;
        dataObject = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            result = OleGetClipboard(out dataObject);
            if (result >= 0)
            {
                return result;
            }

            Thread.Sleep(20);
        }

        return result;
    }

    internal static int SetClipboardWithRetry(ComIDataObject? dataObject)
    {
        int result = 0;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            result = OleSetClipboard(dataObject);
            if (result >= 0)
            {
                return result;
            }

            Thread.Sleep(20);
        }

        return result;
    }

    [DllImport("ole32.dll")]
    internal static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll")]
    internal static extern void OleUninitialize();

    [DllImport("ole32.dll")]
    internal static extern int OleGetClipboard(
        [MarshalAs(UnmanagedType.Interface)] out ComIDataObject? dataObject);

    [DllImport("ole32.dll")]
    internal static extern int OleSetClipboard(
        [MarshalAs(UnmanagedType.Interface)] ComIDataObject? dataObject);

    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();
}

internal static class PasteNative
{
    internal const ushort ControlKey = 0x11;
    internal const ushort VKey = 0x56;
    private const uint KeyboardInputType = 1;
    private const uint KeyUp = 0x0002;
    private const int ShiftKey = 0x10;
    private const int MenuKey = 0x12;
    private const int LeftWindowsKey = 0x5B;
    private const int RightWindowsKey = 0x5C;
    private const nuint SoltexPasteTag = 0x534F4C54;

    internal static bool ModifierIsDown() =>
        IsDown(ControlKey) ||
        IsDown(ShiftKey) ||
        IsDown(MenuKey) ||
        IsDown(LeftWindowsKey) ||
        IsDown(RightWindowsKey);

    internal static void ReleaseControl()
    {
        Input[] cleanup = [Input.Keyboard(ControlKey, keyUp: true)];
        _ = SendInput(1, cleanup, Marshal.SizeOf<Input>());
    }

    private static bool IsDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        internal uint Type;
        internal InputUnion Data;

        internal static Input Keyboard(
            ushort virtualKey,
            bool keyUp,
            nuint extraInfo = SoltexPasteTag) => new()
        {
            Type = KeyboardInputType,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyUp : 0,
                    ExtraInfo = extraInfo
                }
            }
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        internal KeyboardInput Keyboard;

        [FieldOffset(0)]
        internal MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(
        uint inputCount,
        [In] Input[] inputs,
        int inputSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
