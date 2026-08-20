using System.Buffers;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Soltex.Security;

namespace Soltex.Whisper.Windows;

/// <summary>
/// Owns the first local Whisper model beneath the canonical Soltex data root. The
/// model is never bundled and network access occurs only from InstallAsync or
/// RepairAsync after a user action reaches this boundary.
/// </summary>
public sealed class WindowsWhisperLocalModelManager : IWhisperModelManager
{
    public static TimeSpan MaximumInstallDuration { get; } = TimeSpan.FromMinutes(30);

    public static int TransferBufferBytes => 128 * 1024;

    public static string PinnedUpstreamRevision =>
        WhisperLocalModelArtifact.TurboQ5Cpu.UpstreamRevision;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly WhisperLocalModelArtifact _artifact;
    private readonly string _modelsRoot;
    private readonly string _targetPath;
    private readonly string _lockPath;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _statusLock = new();
    private WhisperModelStatus _lastStatus;
    private int _disposeStarted;

    public WindowsWhisperLocalModelManager(string canonicalDataRoot)
        : this(
            canonicalDataRoot,
            CreateHttpClient(),
            WhisperLocalModelArtifact.TurboQ5Cpu,
            ownsHttpClient: true)
    {
    }

    internal WindowsWhisperLocalModelManager(
        string canonicalDataRoot,
        HttpClient httpClient,
        WhisperLocalModelArtifact artifact,
        bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(artifact);
        artifact.Validate();

        string dataRoot = PathSafety.NormalizeExistingDirectory(canonicalDataRoot);
        string whisperRoot = EnsureOwnedDirectory(dataRoot, "whisper");
        _modelsRoot = EnsureOwnedDirectory(whisperRoot, "models");
        _targetPath = PathSafety.CombineUnderRoot(_modelsRoot, artifact.FileName);
        _lockPath = PathSafety.CombineUnderRoot(_modelsRoot, ".install.lock");
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _artifact = artifact;
        _lastStatus = CreateStatus(
            WhisperModelInstallState.NotInstalled,
            installedBytes: 0,
            WhisperModelFailureKind.None,
            failureReason: null);
    }

    public async ValueTask<WhisperModelStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return ReadStatus();
        }

        try
        {
            WhisperModelStatus status = await InspectOwnedModelAsync(cancellationToken)
                .ConfigureAwait(false);
            WhisperModelStatus previous = ReadStatus();
            if (status.State == WhisperModelInstallState.NotInstalled &&
                status.FailureKind == WhisperModelFailureKind.None &&
                previous.FailureKind != WhisperModelFailureKind.None)
            {
                status = status with
                {
                    FailureKind = previous.FailureKind,
                    FailureReason = previous.FailureReason
                };
            }

            Publish(status);
            return status;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public ValueTask<WhisperModelStatus> InstallAsync(
        IProgress<WhisperModelInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallCoreAsync(repair: false, progress, cancellationToken);

    public ValueTask<WhisperModelStatus> RepairAsync(
        IProgress<WhisperModelInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallCoreAsync(repair: true, progress, cancellationToken);

    public async ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw Failure(
                WhisperModelFailureKind.Busy,
                "Another local model operation is already running.");
        }

        try
        {
            await using FileStream installLock = OpenInstallLock();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureRootIdentity();
            if (File.Exists(_targetPath))
            {
                RejectReparseFile(_targetPath);
                File.Delete(_targetPath);
            }

            CleanupInterruptedDownloads();
            Publish(CreateStatus(
                WhisperModelInstallState.NotInstalled,
                installedBytes: 0,
                WhisperModelFailureKind.None,
                failureReason: null));
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Failure(
                WhisperModelFailureKind.AccessDenied,
                "Windows denied access to the local model store.",
                exception);
        }
        catch (IOException exception)
        {
            throw Failure(
                WhisperModelFailureKind.StorageUnavailable,
                "The local model could not be deleted from its owned store.",
                exception);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 0)
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_ownsHttpClient)
                {
                    _httpClient.Dispose();
                }
            }
            finally
            {
                _operationGate.Release();
                _operationGate.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask<WhisperModelStatus> InstallCoreAsync(
        bool repair,
        IProgress<WhisperModelInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw Failure(
                WhisperModelFailureKind.Busy,
                "Another local model operation is already running.");
        }

        WhisperModelStatus previous = ReadStatus();
        try
        {
            previous = await InspectOwnedModelAsync(cancellationToken).ConfigureAwait(false);
            if (!repair && previous.State == WhisperModelInstallState.Ready)
            {
                Publish(previous);
                return previous;
            }

            await using FileStream installLock = OpenInstallLock();
            CleanupInterruptedDownloads();
            Publish(CreateStatus(
                WhisperModelInstallState.Installing,
                installedBytes: 0,
                WhisperModelFailureKind.None,
                failureReason: null));

            using CancellationTokenSource deadline =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(MaximumInstallDuration);
            WhisperModelStatus installed = await DownloadAndPromoteAsync(
                progress,
                deadline.Token).ConfigureAwait(false);
            Publish(installed);
            return installed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(previous with
            {
                FailureKind = WhisperModelFailureKind.Cancelled,
                FailureReason = "The local model operation was cancelled."
            });
            throw;
        }
        catch (OperationCanceledException exception)
        {
            WhisperModelInstallException failure = Failure(
                WhisperModelFailureKind.Network,
                "The local model download did not finish within the bounded install window.",
                exception);
            PublishFault(failure, previous.InstalledBytes);
            throw failure;
        }
        catch (WhisperModelInstallException exception)
        {
            PublishFault(exception, previous.InstalledBytes);
            throw;
        }
        catch (HttpRequestException exception)
        {
            WhisperModelInstallException failure = Failure(
                WhisperModelFailureKind.Network,
                "The local model download could not be completed.",
                exception);
            PublishFault(failure, previous.InstalledBytes);
            throw failure;
        }
        catch (UnauthorizedAccessException exception)
        {
            WhisperModelInstallException failure = Failure(
                WhisperModelFailureKind.AccessDenied,
                "Windows denied access to the local model store.",
                exception);
            PublishFault(failure, previous.InstalledBytes);
            throw failure;
        }
        catch (IOException exception)
        {
            WhisperModelInstallException failure = Failure(
                WhisperModelFailureKind.StorageUnavailable,
                "The local model store could not complete the operation.",
                exception);
            PublishFault(failure, previous.InstalledBytes);
            throw failure;
        }
        catch (Win32Exception exception)
        {
            WhisperModelInstallException failure = Failure(
                WhisperModelFailureKind.OwnershipChanged,
                "The local model file identity could not be confirmed.",
                exception);
            PublishFault(failure, previous.InstalledBytes);
            throw failure;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask<WhisperModelStatus> DownloadAndPromoteAsync(
        IProgress<WhisperModelInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureRootIdentity();
        string temporaryPath = CreateTemporaryPath();
        bool promoted = false;
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, _artifact.DownloadUri);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw Failure(
                    WhisperModelFailureKind.ResponseRejected,
                    "The pinned model source returned an unexpected response.");
            }

            if (response.Content.Headers.ContentEncoding.Count > 0 ||
                response.Content.Headers.ContentLength != _artifact.ExpectedBytes)
            {
                throw Failure(
                    WhisperModelFailureKind.SizeMismatch,
                    "The pinned model response did not declare the expected exact size.");
            }

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using FileStream destination = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                TransferBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            WindowsWhisperFileIdentity identity = WindowsWhisperFileIdentity.From(destination.SafeFileHandle);
            using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(TransferBufferBytes);
            long received = 0;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int allowed = checked((int)Math.Min(
                        buffer.Length,
                        _artifact.ExpectedBytes - received + 1));
                    int read = await source.ReadAsync(
                        buffer.AsMemory(0, allowed),
                        cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    received += read;
                    if (received > _artifact.ExpectedBytes)
                    {
                        throw Failure(
                            WhisperModelFailureKind.SizeMismatch,
                            "The local model response exceeded its expected size.");
                    }

                    digest.AppendData(buffer, 0, read);
                    await destination.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken).ConfigureAwait(false);
                    progress?.Report(new WhisperModelInstallProgress(
                        received,
                        _artifact.ExpectedBytes));
                    Publish(CreateStatus(
                        WhisperModelInstallState.Installing,
                        received,
                        WhisperModelFailureKind.None,
                        failureReason: null));
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (received != _artifact.ExpectedBytes || destination.Length != _artifact.ExpectedBytes)
            {
                throw Failure(
                    WhisperModelFailureKind.SizeMismatch,
                    "The local model response ended before its expected exact size.");
            }

            byte[] expectedDigest = Convert.FromHexString(_artifact.ExpectedSha256);
            byte[] observedDigest = digest.GetHashAndReset();
            bool digestMatches = CryptographicOperations.FixedTimeEquals(
                expectedDigest,
                observedDigest);
            CryptographicOperations.ZeroMemory(expectedDigest);
            CryptographicOperations.ZeroMemory(observedDigest);
            if (!digestMatches)
            {
                throw Failure(
                    WhisperModelFailureKind.DigestMismatch,
                    "The downloaded model did not match the pinned upstream digest.");
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
            await destination.DisposeAsync().ConfigureAwait(false);
            EnsureRootIdentity();
            RejectReparseFile(temporaryPath);
            RejectReparseFile(_targetPath, allowMissing: true);
            File.Move(temporaryPath, _targetPath, overwrite: true);
            promoted = true;
            RejectReparseFile(_targetPath);
            await using FileStream promotedFile = new(
                _targetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                TransferBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (promotedFile.Length != _artifact.ExpectedBytes ||
                WindowsWhisperFileIdentity.From(promotedFile.SafeFileHandle) != identity)
            {
                throw Failure(
                    WhisperModelFailureKind.OwnershipChanged,
                    "The local model path identity changed during promotion.");
            }

            progress?.Report(new WhisperModelInstallProgress(
                _artifact.ExpectedBytes,
                _artifact.ExpectedBytes));
            return CreateStatus(
                WhisperModelInstallState.Ready,
                _artifact.ExpectedBytes,
                WhisperModelFailureKind.None,
                failureReason: null);
        }
        finally
        {
            if (!promoted)
            {
                TryDeleteOwnedTemporary(temporaryPath);
            }
        }
    }

    private async ValueTask<WhisperModelStatus> InspectOwnedModelAsync(
        CancellationToken cancellationToken)
    {
        EnsureRootIdentity();
        if (!File.Exists(_targetPath))
        {
            return CreateStatus(
                WhisperModelInstallState.NotInstalled,
                installedBytes: 0,
                WhisperModelFailureKind.None,
                failureReason: null);
        }

        try
        {
            RejectReparseFile(_targetPath);
            await using FileStream stream = new(
                _targetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                TransferBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            long length = stream.Length;
            if (length != _artifact.ExpectedBytes)
            {
                return CreateStatus(
                    WhisperModelInstallState.Invalid,
                    Math.Max(0, length),
                    WhisperModelFailureKind.SizeMismatch,
                    "The installed model has an unexpected size.");
            }

            using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(TransferBufferBytes);
            long received = 0;
            try
            {
                while (received < _artifact.ExpectedBytes)
                {
                    int read = await stream.ReadAsync(
                        buffer.AsMemory(
                            0,
                            checked((int)Math.Min(
                                buffer.Length,
                                _artifact.ExpectedBytes - received))),
                        cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return CreateStatus(
                            WhisperModelInstallState.Invalid,
                            received,
                            WhisperModelFailureKind.SizeMismatch,
                            "The installed model ended unexpectedly during verification.");
                    }

                    digest.AppendData(buffer, 0, read);
                    received += read;
                }

                if (await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
                        .ConfigureAwait(false) != 0)
                {
                    return CreateStatus(
                        WhisperModelInstallState.Invalid,
                        received + 1,
                        WhisperModelFailureKind.SizeMismatch,
                        "The installed model changed during verification.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
                ArrayPool<byte>.Shared.Return(buffer);
            }

            byte[] expectedDigest = Convert.FromHexString(_artifact.ExpectedSha256);
            byte[] observedDigest = digest.GetHashAndReset();
            bool matches = CryptographicOperations.FixedTimeEquals(
                expectedDigest,
                observedDigest);
            CryptographicOperations.ZeroMemory(expectedDigest);
            CryptographicOperations.ZeroMemory(observedDigest);
            return matches
                ? CreateStatus(
                    WhisperModelInstallState.Ready,
                    received,
                    WhisperModelFailureKind.None,
                    failureReason: null)
                : CreateStatus(
                    WhisperModelInstallState.Invalid,
                    received,
                    WhisperModelFailureKind.DigestMismatch,
                    "The installed model does not match the pinned upstream digest.");
        }
        catch (UnauthorizedAccessException)
        {
            return CreateStatus(
                WhisperModelInstallState.Faulted,
                installedBytes: 0,
                WhisperModelFailureKind.AccessDenied,
                "Windows denied access while the local model was verified.");
        }
        catch (IOException)
        {
            return CreateStatus(
                WhisperModelInstallState.Faulted,
                installedBytes: 0,
                WhisperModelFailureKind.StorageUnavailable,
                "The local model store could not be read safely.");
        }
    }

    private FileStream OpenInstallLock()
    {
        try
        {
            RejectReparseFile(_lockPath, allowMissing: true);
            FileStream stream = new(
                _lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            try
            {
                RejectReparseFile(_lockPath);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (IOException exception)
        {
            throw Failure(
                WhisperModelFailureKind.Busy,
                "Another Soltex process owns the local model installer.",
                exception);
        }
    }

    private void CleanupInterruptedDownloads()
    {
        EnsureRootIdentity();
        foreach (string candidate in Directory.EnumerateFiles(
                     _modelsRoot,
                     _artifact.FileName + ".*.partial",
                     SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(candidate);
            string prefix = _artifact.FileName + ".";
            string token = fileName[prefix.Length..^".partial".Length];
            if (token.Length != 32 || !token.All(Uri.IsHexDigit))
            {
                continue;
            }

            RejectReparseFile(candidate);
            File.Delete(candidate);
        }
    }

    private string CreateTemporaryPath() => PathSafety.CombineUnderRoot(
        _modelsRoot,
        _artifact.FileName + "." + Guid.NewGuid().ToString("N") + ".partial");

    private void EnsureRootIdentity()
    {
        string observed = PathSafety.NormalizeExistingDirectory(_modelsRoot);
        if (!string.Equals(observed, _modelsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                WhisperModelFailureKind.OwnershipChanged,
                "The local model store identity changed during the operation.");
        }
    }

    private static string EnsureOwnedDirectory(string parent, string name)
    {
        string candidate = PathSafety.CombineUnderRoot(parent, name);
        if (File.Exists(candidate) && !Directory.Exists(candidate))
        {
            throw new InvalidDataException(
                "The local model directory collides with a non-directory entry.");
        }

        _ = Directory.CreateDirectory(candidate);
        return PathSafety.NormalizeExistingDirectory(candidate);
    }

    private static void RejectReparseFile(string path, bool allowMissing = false)
    {
        if (!File.Exists(path))
        {
            if (allowMissing)
            {
                return;
            }

            throw new FileNotFoundException("The owned model artifact is unavailable.");
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw Failure(
                WhisperModelFailureKind.OwnershipChanged,
                "Reparse-point model artifacts are not accepted.");
        }
    }

    private static void TryDeleteOwnedTemporary(string path)
    {
        try
        {
            if (File.Exists(path) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private WhisperModelStatus CreateStatus(
        WhisperModelInstallState state,
        long installedBytes,
        WhisperModelFailureKind failureKind,
        string? failureReason) => new(
        _artifact.ProviderId,
        _artifact.ModelId,
        _artifact.RuntimeId,
        state,
        _artifact.ExpectedBytes,
        installedBytes,
        failureKind,
        failureReason);

    private void PublishFault(WhisperModelInstallException failure, long installedBytes)
    {
        WhisperModelInstallState state = failure.Kind is
            WhisperModelFailureKind.DigestMismatch or
            WhisperModelFailureKind.SizeMismatch
                ? WhisperModelInstallState.Invalid
                : WhisperModelInstallState.Faulted;
        Publish(CreateStatus(state, installedBytes, failure.Kind, failure.Message));
    }

    private void Publish(WhisperModelStatus status)
    {
        lock (_statusLock)
        {
            _lastStatus = status;
        }
    }

    private WhisperModelStatus ReadStatus()
    {
        lock (_statusLock)
        {
            return _lastStatus;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(
        Volatile.Read(ref _disposeStarted) != 0,
        this);

    private static WhisperModelInstallException Failure(
        WhisperModelFailureKind kind,
        string message,
        Exception? inner = null) => new(kind, message, inner);

    private static HttpClient CreateHttpClient()
    {
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            MaxAutomaticRedirections = 5,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}

internal readonly record struct WindowsWhisperFileIdentity(
    uint VolumeSerialNumber,
    ulong FileIndex)
{
    internal static WindowsWhisperFileIdentity From(SafeFileHandle handle)
    {
        if (!WindowsWhisperFileIdentityNative.GetFileInformationByHandle(
                handle,
                out WindowsWhisperFileIdentityNative.ByHandleFileInformation information))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "The owned model file identity could not be read.");
        }

        return new WindowsWhisperFileIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }
}

internal static class WindowsWhisperFileIdentityNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);
}
