using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Soltex.Security;

internal static class Dpapi
{
    private const uint CryptProtectUiForbidden = 0x1;
    private static readonly byte[] ZeroBuffer = new byte[4 * 1024];

    public static byte[] Protect(byte[] clearText) => Transform(clearText, protect: true);

    public static byte[] Unprotect(byte[] protectedData) => Transform(protectedData, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");
        }

        nint inputPointer = Marshal.AllocHGlobal(input.Length);
        try
        {
            Marshal.Copy(input, 0, inputPointer, input.Length);
            DataBlob inputBlob = new() { Size = input.Length, Data = inputPointer };
            bool succeeded;
            DataBlob outputBlob;

            if (protect)
            {
                succeeded = NativeMethods.CryptProtectData(
                    ref inputBlob,
                    ProductIdentity.LegacyDpapiDescription,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);
            }
            else
            {
                succeeded = NativeMethods.CryptUnprotectData(
                    ref inputBlob,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);
            }

            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                byte[] output = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return output;
            }
            finally
            {
                ClearUnmanaged(outputBlob.Data, outputBlob.Size);
                _ = NativeMethods.LocalFree(outputBlob.Data);
            }
        }
        finally
        {
            ClearUnmanaged(inputPointer, input.Length);
            Marshal.FreeHGlobal(inputPointer);
        }
    }

    private static void ClearUnmanaged(nint memory, int byteCount)
    {
        int offset = 0;
        while (offset < byteCount)
        {
            int count = Math.Min(ZeroBuffer.Length, byteCount - offset);
            Marshal.Copy(ZeroBuffer, 0, memory + offset, count);
            offset += count;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        internal int Size;
        internal nint Data;
    }

    private static class NativeMethods
    {
        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string description,
            nint optionalEntropy,
            nint reserved,
            nint promptStructure,
            uint flags,
            out DataBlob dataOut);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            nint description,
            nint optionalEntropy,
            nint reserved,
            nint promptStructure,
            uint flags,
            out DataBlob dataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint LocalFree(nint memory);
    }
}
