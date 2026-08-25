using System.Security;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Soltex.Capture;

public sealed record CaptureScreenshotRequest(
    nint OwnerWindow,
    string OutputRoot,
    bool IncludePointer = true);

public sealed record CaptureScreenshotResult(
    CaptureStoragePlan Storage,
    ClipManifest Manifest,
    string SourceLabel);

public sealed class CaptureScreenshotException : InvalidOperationException
{
    public CaptureScreenshotException(CaptureFailureKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public CaptureScreenshotException(
        CaptureFailureKind kind,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public CaptureFailureKind Kind { get; }
}

public sealed class WindowsGraphicsCaptureScreenshotService
{
    public const int MaximumDimension = 16_384;
    public const long MaximumPixelCount = 35_389_440;
    public const long MaximumEncodedBytes = 256L * 1_024 * 1_024;
    public static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(12);

    public static CaptureCapabilitySnapshot Probe()
    {
        bool platform = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
        bool graphicsCapture = false;
        string limitation = string.Empty;
        if (platform)
        {
            try
            {
                graphicsCapture = GraphicsCaptureSession.IsSupported();
            }
            catch (Exception exception) when (IsExpectedPlatformFailure(exception))
            {
                limitation = "Windows screen capture is unavailable in this session.";
            }
        }

        return new CaptureCapabilitySnapshot(
            DateTimeOffset.UtcNow,
            [
                new CaptureCapability(
                    "screenshot",
                    graphicsCapture,
                    RequiresConsent: true,
                    graphicsCapture
                        ? "Windows will ask you to choose a display or window."
                        : "Windows screen capture is unavailable."),
                new CaptureCapability(
                    "recording",
                    Available: false,
                    RequiresConsent: true,
                    "Video recording is not enabled yet."),
                new CaptureCapability(
                    "replay",
                    Available: false,
                    RequiresConsent: true,
                    "Instant replay is off and unavailable until recording passes its runtime gates.")
            ],
            platform ? limitation : "Capture requires Windows 10 version 2004 or later.");
    }

    public static async Task<CaptureScreenshotResult?> CaptureAsync(
        CaptureScreenshotRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputRoot);
        if (request.OwnerWindow == 0)
        {
            throw new ArgumentException("A visible owner window is required.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!Probe().IsAvailable("screenshot"))
        {
            throw new CaptureScreenshotException(
                CaptureFailureKind.Unsupported,
                "Windows screen capture is unavailable.");
        }

        GraphicsCapturePicker picker = new();
        InitializeWithWindow.Initialize(picker, request.OwnerWindow);
        GraphicsCaptureItem? item;
        try
        {
            item = await picker.PickSingleItemAsync().AsTask(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedPlatformFailure(exception))
        {
            throw MapFailure(exception, "Windows could not open the capture picker.");
        }

        if (item is null)
        {
            return null;
        }

        ValidateSize(item.Size.Width, item.Size.Height);
        string sourceLabel = CaptureCapabilitySnapshot.NormalizeText(item.DisplayName, 96);
        if (sourceLabel.Length == 0)
        {
            sourceLabel = "Selected source";
        }

        using ID3D11Device device = D3D11.D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport);
        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        IDirect3DDevice winRtDevice =
            D3D11.CreateDirect3D11DeviceFromDXGIDevice<IDirect3DDevice>(dxgiDevice);
        try
        {
            using Direct3D11CaptureFramePool framePool =
                Direct3D11CaptureFramePool.CreateFreeThreaded(
                    winRtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    1,
                    item.Size);
            using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = request.IncludePointer;

            using Direct3D11CaptureFrame frame = await WaitForFrameAsync(
                item,
                framePool,
                session,
                cancellationToken);
            ValidateSize(frame.ContentSize.Width, frame.ContentSize.Height);

            using SoftwareBitmap softwareBitmap =
                await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface)
                    .AsTask(cancellationToken);
            if (softwareBitmap.PixelWidth != frame.ContentSize.Width ||
                softwareBitmap.PixelHeight != frame.ContentSize.Height)
            {
                throw new CaptureScreenshotException(
                    CaptureFailureKind.DeviceLost,
                    "The selected source changed size. Try the screenshot again.");
            }
            return await SavePngAsync(
                softwareBitmap,
                frame.ContentSize.Width,
                frame.ContentSize.Height,
                request.OutputRoot,
                sourceLabel,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CaptureScreenshotException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedPlatformFailure(exception))
        {
            throw MapFailure(exception, "Windows could not capture the selected source.");
        }
        finally
        {
            if (winRtDevice is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static async Task<Direct3D11CaptureFrame> WaitForFrameAsync(
        GraphicsCaptureItem item,
        Direct3D11CaptureFramePool framePool,
        GraphicsCaptureSession session,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<Direct3D11CaptureFrame> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFrameArrived(
            Direct3D11CaptureFramePool sender,
            object args)
        {
            Direct3D11CaptureFrame? frame = null;
            try
            {
                frame = sender.TryGetNextFrame();
                if (frame is not null && completion.TrySetResult(frame))
                {
                    frame = null;
                }
            }
            catch (Exception exception) when (IsExpectedPlatformFailure(exception))
            {
                completion.TrySetException(MapFailure(exception, "The capture source stopped responding."));
            }
            finally
            {
                frame?.Dispose();
            }
        }

        void OnClosed(GraphicsCaptureItem sender, object args) =>
            completion.TrySetException(new CaptureScreenshotException(
                CaptureFailureKind.SourceClosed,
                "The selected capture source closed."));

        framePool.FrameArrived += OnFrameArrived;
        item.Closed += OnClosed;
        try
        {
            session.StartCapture();
            return await completion.Task.WaitAsync(FrameTimeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new CaptureScreenshotException(
                CaptureFailureKind.DeviceLost,
                "The selected source did not provide a frame in time.",
                exception);
        }
        finally
        {
            completion.TrySetCanceled(CancellationToken.None);
            framePool.FrameArrived -= OnFrameArrived;
            item.Closed -= OnClosed;
        }
    }

    private static async Task<CaptureScreenshotResult> SavePngAsync(
        SoftwareBitmap bitmap,
        int width,
        int height,
        string outputRoot,
        string sourceLabel,
        CancellationToken cancellationToken)
    {
        using InMemoryRandomAccessStream encoded = new();
        BitmapEncoder encoder = await BitmapEncoder
            .CreateAsync(BitmapEncoder.PngEncoderId, encoded)
            .AsTask(cancellationToken);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(cancellationToken);

        if (encoded.Size is 0 || encoded.Size > MaximumEncodedBytes)
        {
            throw new CaptureScreenshotException(
                CaptureFailureKind.StorageExhausted,
                "The screenshot exceeded its output bound.");
        }

        Guid clipId = Guid.NewGuid();
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
        CaptureStoragePlan? storage = null;
        string? temporaryPath = null;
        long encodedBytes = 0;
        string sha256 = string.Empty;
        try
        {
            storage = CaptureStoragePolicy.PlanOwnedFile(
                outputRoot,
                CaptureOutputKind.Screenshot,
                capturedAtUtc,
                clipId);
            Directory.CreateDirectory(storage.RootPath);
            temporaryPath =
                storage.FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            encoded.Seek(0);
            await using (Stream source = encoded.AsStreamForRead())
            await using (FileStream destination = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(destination, 81_920, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                encodedBytes = destination.Length;
                if (encodedBytes is <= 0 or > MaximumEncodedBytes)
                {
                    throw new CaptureScreenshotException(
                        CaptureFailureKind.StorageExhausted,
                        "The screenshot exceeded its output bound.");
                }

                destination.Position = 0;
                byte[] hash = await SHA256.HashDataAsync(destination, cancellationToken);
                sha256 = Convert.ToHexStringLower(hash);
            }

            File.Move(temporaryPath, storage.FilePath, overwrite: false);
            ClipManifest manifest = new ClipManifest(
                clipId,
                CaptureOutputKind.Screenshot,
                capturedAtUtc,
                TimeSpan.Zero,
                width,
                height,
                0,
                CaptureAudioMode.None,
                "Windows Graphics Capture / PNG",
                encodedBytes,
                sha256,
                storage.FileName).Normalize();
            return new CaptureScreenshotResult(storage, manifest, sourceLabel);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CaptureScreenshotException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            throw new CaptureScreenshotException(
                exception is UnauthorizedAccessException or SecurityException
                    ? CaptureFailureKind.StorageUnavailable
                    : CaptureFailureKind.StorageExhausted,
                "The screenshot could not be saved.",
                exception);
        }
        finally
        {
            try
            {
                if (temporaryPath is not null && File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (IsExpectedStorageFailure(exception))
            {
                // Cleanup is best effort for the exact owned temporary artifact only.
            }
        }
    }

    internal static void ValidateSize(int width, int height)
    {
        long pixels = (long)width * height;
        if (width <= 0 ||
            height <= 0 ||
            width > MaximumDimension ||
            height > MaximumDimension ||
            pixels <= 0 ||
            pixels > MaximumPixelCount)
        {
            throw new CaptureScreenshotException(
                CaptureFailureKind.ProtectedContent,
                "The selected source did not provide a supported frame size.");
        }
    }

    private static CaptureScreenshotException MapFailure(Exception exception, string message)
    {
        CaptureFailureKind kind = exception.HResult switch
        {
            unchecked((int)0x80070005) => CaptureFailureKind.ConsentDeclined,
            unchecked((int)0x887A0005) or unchecked((int)0x887A0006) or unchecked((int)0x887A0007) =>
                CaptureFailureKind.DeviceLost,
            unchecked((int)0x8007000E) => CaptureFailureKind.StorageExhausted,
            _ => CaptureFailureKind.Unexpected
        };
        return new CaptureScreenshotException(kind, message, exception);
    }

    private static bool IsExpectedPlatformFailure(Exception exception) =>
        exception is UnauthorizedAccessException or COMException or ObjectDisposedException or InvalidOperationException;

    private static bool IsExpectedStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException;
}
