using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Soltex.Audio;

// Minimal Core Audio (MMDevice) declarations. Only the reads Soltex needs are
// bound; the remaining vtable slots are declared so the interface layout stays
// correct, and are never invoked. Every method is PreserveSig so a failing
// endpoint degrades into an explicit "inaccessible" count instead of throwing.

internal enum DataFlow
{
    Render = 0,
    Capture = 1,
    All = 2
}

internal enum DeviceRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

internal static class DeviceStateMask
{
    internal const uint Active = 0x1;
    internal const uint Disabled = 0x2;
    internal const uint NotPresent = 0x4;
    internal const uint Unplugged = 0x8;
    internal const uint All = 0xF;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    internal Guid FormatId;
    internal uint PropertyId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    internal ushort VariantType;
    internal ushort Reserved1;
    internal ushort Reserved2;
    internal ushort Reserved3;
    internal IntPtr Value;
    internal IntPtr ValueHigh;

    internal const ushort VtLpwstr = 31;

    internal readonly string? ReadString() =>
        VariantType == VtLpwstr && Value != IntPtr.Zero
            ? Marshal.PtrToStringUni(Value)
            : null;
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MMDeviceEnumeratorObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(DataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(DataFlow dataFlow, DeviceRole role, out IMMDevice device);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [PreserveSig]
    int OpenPropertyStore(uint storageAccess, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant value);

    [PreserveSig]
    int Commit();
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid eventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float level);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

internal static class CoreAudio
{
    internal const int Ok = 0;
    internal const uint StorageRead = 0;
    internal const uint ClassContextAll = 0x17;

    /// <summary>PKEY_Device_FriendlyName.</summary>
    internal static PropertyKey FriendlyNameKey => new()
    {
        FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        PropertyId = 14
    };

    internal static Guid AudioEndpointVolumeId => new("5CDF2C82-841E-4546-9722-0CF74078229A");

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant value);
}
