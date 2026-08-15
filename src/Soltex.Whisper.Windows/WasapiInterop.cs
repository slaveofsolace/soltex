using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Soltex.Whisper.Windows;

internal enum WasapiDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2
}

internal enum WasapiDeviceRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

internal enum AudioClientShareMode
{
    Shared = 0,
    Exclusive = 1
}

internal static class WasapiDeviceState
{
    internal const uint Active = 0x1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WasapiPropertyKey
{
    internal Guid FormatId;
    internal uint PropertyId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WasapiPropVariant
{
    internal ushort VariantType;
    internal ushort Reserved1;
    internal ushort Reserved2;
    internal ushort Reserved3;
    internal IntPtr Value;
    internal IntPtr ValueHigh;

    internal readonly string? ReadString() =>
        VariantType == 31 && Value != IntPtr.Zero
            ? Marshal.PtrToStringUni(Value)
            : null;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx
{
    internal ushort FormatTag;
    internal ushort Channels;
    internal uint SamplesPerSecond;
    internal uint AverageBytesPerSecond;
    internal ushort BlockAlign;
    internal ushort BitsPerSample;
    internal ushort ExtraSize;
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class WasapiDeviceEnumeratorObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWasapiDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(
        WasapiDataFlow dataFlow,
        uint stateMask,
        out IWasapiDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(
        WasapiDataFlow dataFlow,
        WasapiDeviceRole role,
        out IWasapiDevice device);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IWasapiDevice device);

    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWasapiDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IWasapiDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWasapiDevice
{
    [PreserveSig]
    int Activate(
        ref Guid interfaceId,
        uint classContext,
        IntPtr activationParameters,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [PreserveSig]
    int OpenPropertyStore(uint storageAccess, out IWasapiPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig] int GetState(out uint state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWasapiPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out WasapiPropertyKey key);
    [PreserveSig] int GetValue(ref WasapiPropertyKey key, out WasapiPropVariant value);
    [PreserveSig] int SetValue(ref WasapiPropertyKey key, ref WasapiPropVariant value);
    [PreserveSig] int Commit();
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(
        AudioClientShareMode shareMode,
        uint streamFlags,
        long bufferDuration,
        long periodicity,
        IntPtr format,
        IntPtr audioSessionGuid);

    [PreserveSig] int GetBufferSize(out uint bufferFrameCount);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint paddingFrameCount);
    [PreserveSig] int IsFormatSupported(AudioClientShareMode shareMode, IntPtr format, out IntPtr closestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
    [PreserveSig] int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig]
    int GetBuffer(
        out IntPtr data,
        out uint frameCount,
        out uint flags,
        out ulong devicePosition,
        out ulong performanceCounterPosition);

    [PreserveSig] int ReleaseBuffer(uint frameCount);
    [PreserveSig] int GetNextPacketSize(out uint frameCount);
}

internal static class WasapiNative
{
    internal const int Ok = 0;
    internal const uint ClassContextAll = 0x17;
    internal const uint StorageRead = 0;
    internal const uint StreamFlagsNoPersist = 0x0008_0000;
    internal const uint BufferFlagsDataDiscontinuity = 0x1;
    internal const uint BufferFlagsSilent = 0x2;
    internal const uint BufferFlagsTimestampError = 0x4;

    internal const int AccessDenied = unchecked((int)0x80070005);
    internal const int NotFound = unchecked((int)0x80070490);
    internal const int DeviceInUse = unchecked((int)0x8889000A);
    internal const int DeviceInvalidated = unchecked((int)0x88890004);
    internal const int EndpointCreateFailed = unchecked((int)0x8889000F);

    internal static Guid AudioClientId => new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    internal static Guid AudioCaptureClientId => new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    internal static Guid PcmSubFormat => new("00000001-0000-0010-8000-00AA00389B71");
    internal static Guid FloatSubFormat => new("00000003-0000-0010-8000-00AA00389B71");

    internal static WasapiPropertyKey FriendlyNameKey => new()
    {
        FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        PropertyId = 14
    };

    internal static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            _ = Marshal.ReleaseComObject(instance);
        }
    }

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref WasapiPropVariant value);
}
