using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Win32.SafeHandles;
using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

/// <summary>
/// Metadata-only UI Automation reader. It deliberately never requests Name,
/// ValuePattern.Value, text ranges, selected text, captions, or surrounding text.
/// </summary>
internal sealed class WindowsUiAutomationTargetBackend : IWhisperTargetInspectionBackend
{
    private const int TextPattern2Id = 10024;

    public WindowsWhisperTargetObservation InspectFocused()
    {
        AutomationElement element = AutomationElement.FocusedElement ??
            throw new InvalidOperationException("Windows did not expose a focused automation element.");
        AutomationElement.AutomationElementInformation current = element.Current;
        int processId = current.ProcessId;
        string processName = ReadProcessName(processId);
        int[] runtimeId = element.GetRuntimeId();
        bool isPassword = current.IsPassword;
        AutomationPattern[] patterns = element.GetSupportedPatterns();
        bool supportsValuePattern = HasPattern(patterns, ValuePattern.Pattern.Id);
        bool supportsTextPattern = HasPattern(patterns, TextPattern.Pattern.Id);
        bool supportsTextPattern2 = HasPattern(patterns, TextPattern2Id);

        bool valueIsReadOnly = true;
        bool textReadOnlyKnown = false;
        bool textIsReadOnly = true;
        bool supportsSelection = false;

        // Pattern metadata is safe to inspect for protected fields, but provider
        // objects are not opened there. This prevents future code from accidentally
        // extending this path into a value read.
        if (!isPassword &&
            supportsValuePattern &&
            element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObject) &&
            valuePatternObject is ValuePattern valuePattern)
        {
            valueIsReadOnly = valuePattern.Current.IsReadOnly;
        }

        if (!isPassword &&
            supportsTextPattern &&
            element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPatternObject) &&
            textPatternObject is TextPattern textPattern)
        {
            supportsSelection = textPattern.SupportedTextSelection != SupportedTextSelection.None;
            object readOnly = textPattern.DocumentRange.GetAttributeValue(
                TextPattern.IsReadOnlyAttribute);
            if (readOnly is bool readOnlyValue)
            {
                textReadOnlyKnown = true;
                textIsReadOnly = readOnlyValue;
            }
        }

        WindowsWhisperTargetObservation observation = new(
            processId,
            processName,
            current.FrameworkId ?? string.Empty,
            runtimeId,
            WindowsProcessIntegrity.Read(processId),
            MapControlType(current.ControlType),
            current.IsEnabled,
            current.IsKeyboardFocusable,
            isPassword,
            supportsValuePattern,
            valueIsReadOnly,
            supportsTextPattern,
            supportsTextPattern2,
            textReadOnlyKnown,
            textIsReadOnly,
            supportsSelection);

        AutomationElement confirmedElement = AutomationElement.FocusedElement ??
            throw new InvalidOperationException("Focus was lost during target inspection.");
        if (confirmedElement.Current.ProcessId != processId ||
            !confirmedElement.GetRuntimeId().SequenceEqual(runtimeId))
        {
            throw new InvalidOperationException("Focus changed during target inspection.");
        }

        return observation;
    }

    private static string ReadProcessName(int processId)
    {
        using Process process = Process.GetProcessById(processId);
        return process.ProcessName;
    }

    private static bool HasPattern(IEnumerable<AutomationPattern> patterns, int patternId) =>
        patterns.Any(pattern => pattern.Id == patternId);

    private static WindowsWhisperControlKind MapControlType(ControlType controlType)
    {
        if (controlType.Id == ControlType.Edit.Id)
        {
            return WindowsWhisperControlKind.Edit;
        }

        if (controlType.Id == ControlType.Document.Id)
        {
            return WindowsWhisperControlKind.Document;
        }

        return controlType.Id == ControlType.Text.Id
            ? WindowsWhisperControlKind.Text
            : WindowsWhisperControlKind.Unknown;
    }
}

internal static class WindowsProcessIntegrity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int ErrorInsufficientBuffer = 122;

    internal static WhisperTargetIntegrityLevel Read(int processId)
    {
        using SafeProcessHandle process = NativeMethods.OpenProcess(
            ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);
        if (process.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!NativeMethods.OpenProcessToken(process, TokenQuery, out SafeAccessTokenHandle token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        using (token)
        {
            _ = NativeMethods.GetTokenInformation(
                token,
                TokenIntegrityLevel,
                IntPtr.Zero,
                tokenInformationLength: 0,
                out int requiredLength);
            int error = Marshal.GetLastWin32Error();
            if (requiredLength <= 0 || error != ErrorInsufficientBuffer)
            {
                throw new Win32Exception(error);
            }

            IntPtr buffer = Marshal.AllocHGlobal(requiredLength);
            try
            {
                if (!NativeMethods.GetTokenInformation(
                    token,
                    TokenIntegrityLevel,
                    buffer,
                    requiredLength,
                    out _))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                TokenMandatoryLabel label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
                return MapIntegrityRid(ReadLastSubAuthority(label.Label.Sid));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static int ReadLastSubAuthority(IntPtr sid)
    {
        if (sid == IntPtr.Zero)
        {
            throw new InvalidOperationException("The target token did not contain an integrity SID.");
        }

        byte subAuthorityCount = Marshal.ReadByte(sid, 1);
        if (subAuthorityCount == 0)
        {
            throw new InvalidOperationException("The target integrity SID contained no sub-authority.");
        }

        return Marshal.ReadInt32(sid, 8 + ((subAuthorityCount - 1) * sizeof(int)));
    }

    private static WhisperTargetIntegrityLevel MapIntegrityRid(int integrityRid) =>
        integrityRid switch
        {
            < 0x1000 => WhisperTargetIntegrityLevel.Untrusted,
            < 0x2000 => WhisperTargetIntegrityLevel.Low,
            < 0x3000 => WhisperTargetIntegrityLevel.Medium,
            < 0x4000 => WhisperTargetIntegrityLevel.High,
            < 0x5000 => WhisperTargetIntegrityLevel.System,
            _ => WhisperTargetIntegrityLevel.Protected
        };

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SidAndAttributes
    {
        internal readonly IntPtr Sid;
        internal readonly uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TokenMandatoryLabel
    {
        internal readonly SidAndAttributes Label;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(
            SafeProcessHandle processHandle,
            uint desiredAccess,
            out SafeAccessTokenHandle tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(
            SafeAccessTokenHandle tokenHandle,
            int tokenInformationClass,
            IntPtr tokenInformation,
            int tokenInformationLength,
            out int returnLength);
    }
}
