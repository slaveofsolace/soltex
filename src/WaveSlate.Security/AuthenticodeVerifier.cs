using System.Runtime.InteropServices;

namespace WaveSlate.Security;

public enum AuthenticodeStatus
{
    Trusted,
    MissingSignature,
    Untrusted,
    Error
}

public sealed record AuthenticodeVerificationResult(
    AuthenticodeStatus Status,
    int NativeStatus,
    string Detail)
{
    public bool IsTrusted => Status == AuthenticodeStatus.Trusted;
}

public static class AuthenticodeVerifier
{
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeWholeChain = 1;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckChainExcludeRoot = 0x80;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;
    private const uint WtdDisableMd2Md4 = 0x2000;

    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustEExplicitDistrust = unchecked((int)0x800B0111);
    private const int TrustESubjectNotTrusted = unchecked((int)0x800B0004);
    private const int CryptESecuritySettings = unchecked((int)0x80092026);

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static AuthenticodeVerificationResult Verify(
        string path,
        bool allowNetworkRevocationRetrieval = false)
    {
        string fullPath = PathSafety.NormalizeExistingFile(path);
        if (!OperatingSystem.IsWindows())
        {
            return new AuthenticodeVerificationResult(
                AuthenticodeStatus.Error,
                -1,
                "Authenticode verification is only available on Windows.");
        }

        nint pathPointer = nint.Zero;
        nint fileInfoPointer = nint.Zero;
        WinTrustData trustData = default;
        try
        {
            pathPointer = Marshal.StringToCoTaskMemUni(fullPath);
            WinTrustFileInfo fileInfo = new()
            {
                StructureSize = checked((uint)Marshal.SizeOf<WinTrustFileInfo>()),
                FilePath = pathPointer,
                FileHandle = nint.Zero,
                KnownSubject = nint.Zero
            };
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

            uint providerFlags = WtdRevocationCheckChainExcludeRoot | WtdDisableMd2Md4;
            if (!allowNetworkRevocationRetrieval)
            {
                providerFlags |= WtdCacheOnlyUrlRetrieval;
            }

            trustData = new WinTrustData
            {
                StructureSize = checked((uint)Marshal.SizeOf<WinTrustData>()),
                PolicyCallbackData = nint.Zero,
                SipClientData = nint.Zero,
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevokeWholeChain,
                UnionChoice = WtdChoiceFile,
                FileInfo = fileInfoPointer,
                StateAction = WtdStateActionVerify,
                StateData = nint.Zero,
                UrlReference = nint.Zero,
                ProviderFlags = providerFlags,
                UiContext = 0,
                SignatureSettings = nint.Zero
            };

            Guid action = GenericVerifyV2;
            int status = NativeMethods.WinVerifyTrust(new nint(-1), ref action, ref trustData);
            return status switch
            {
                0 => new AuthenticodeVerificationResult(
                    AuthenticodeStatus.Trusted,
                    status,
                    allowNetworkRevocationRetrieval
                        ? "The Authenticode signature and certificate chain are trusted."
                        : "The Authenticode signature and locally cached certificate chain are trusted."),
                TrustENoSignature => new AuthenticodeVerificationResult(
                    AuthenticodeStatus.MissingSignature,
                    status,
                    "The file has no verifiable Authenticode signature."),
                TrustEExplicitDistrust => new AuthenticodeVerificationResult(
                    AuthenticodeStatus.Untrusted,
                    status,
                    "The Authenticode signer is explicitly distrusted."),
                TrustESubjectNotTrusted => new AuthenticodeVerificationResult(
                    AuthenticodeStatus.Untrusted,
                    status,
                    "The Authenticode signature or signer is not trusted."),
                CryptESecuritySettings => new AuthenticodeVerificationResult(
                    AuthenticodeStatus.Untrusted,
                    status,
                    "Local security policy rejected the Authenticode subject."),
                _ => new AuthenticodeVerificationResult(
                    AuthenticodeStatus.Untrusted,
                    status,
                    $"Authenticode verification failed with status 0x{status:X8}.")
            };
        }
        catch (Exception exception) when (exception is OutOfMemoryException or TypeLoadException)
        {
            return new AuthenticodeVerificationResult(AuthenticodeStatus.Error, -1, exception.Message);
        }
        finally
        {
            if (trustData.StructureSize != 0)
            {
                trustData.StateAction = WtdStateActionClose;
                Guid action = GenericVerifyV2;
                _ = NativeMethods.WinVerifyTrust(new nint(-1), ref action, ref trustData);
            }

            if (fileInfoPointer != nint.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }

            if (pathPointer != nint.Zero)
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        internal uint StructureSize;
        internal nint FilePath;
        internal nint FileHandle;
        internal nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        internal uint StructureSize;
        internal nint PolicyCallbackData;
        internal nint SipClientData;
        internal uint UiChoice;
        internal uint RevocationChecks;
        internal uint UnionChoice;
        internal nint FileInfo;
        internal uint StateAction;
        internal nint StateData;
        internal nint UrlReference;
        internal uint ProviderFlags;
        internal uint UiContext;
        internal nint SignatureSettings;
    }

    private static class NativeMethods
    {
        [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
        internal static extern int WinVerifyTrust(
            nint windowHandle,
            ref Guid actionId,
            ref WinTrustData trustData);
    }
}
