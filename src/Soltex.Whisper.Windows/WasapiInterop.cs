using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Soltex.Whisper.Windows;

internal enum AudioClientShareMode
{
    Shared = 0,
    Exclusive = 1
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

    internal static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            _ = Marshal.ReleaseComObject(instance);
        }
    }
}
