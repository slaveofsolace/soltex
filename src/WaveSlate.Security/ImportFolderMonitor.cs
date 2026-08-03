using System.Collections.Concurrent;
using System.Threading.Channels;

namespace WaveSlate.Security;

public sealed class ImportFolderMonitor : IAsyncDisposable
{
    private readonly FileAssessmentService _assessor;
    private readonly Func<FileAssessment, Task> _onAssessment;
    private readonly FileSystemWatcher _watcher;
    private readonly Channel<string> _queue;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _worker;

    public ImportFolderMonitor(
        string folder,
        FileAssessmentService assessor,
        Func<FileAssessment, Task> onAssessment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _assessor = assessor;
        _onAssessment = onAssessment;
        Directory.CreateDirectory(folder);
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.EnableRaisingEvents = true;
        _worker = Task.Run(ProcessQueueAsync);
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => Queue(args.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs args) => Queue(args.FullPath);

    private void Queue(string path)
    {
        if (!FileAssessmentService.ShouldInspectExtension(path))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_recent.TryGetValue(path, out DateTimeOffset previous) && now - previous < TimeSpan.FromMilliseconds(750))
        {
            return;
        }

        _recent[path] = now;
        _queue.Writer.TryWrite(path);
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (string path in _queue.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                await Task.Delay(350, _stopping.Token).ConfigureAwait(false);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    FileAssessment assessment = await _assessor.AssessAsync(path, _stopping.Token).ConfigureAwait(false);
                    await _onAssessment(assessment).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    FileAssessment failed = new(
                        path,
                        string.Empty,
                        0,
                        ContentVerdict.Error,
                        "WaveSlate import monitor",
                        exception.Message,
                        DateTimeOffset.UtcNow);
                    await _onAssessment(failed).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnChanged;
        _watcher.Changed -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Dispose();
        _queue.Writer.TryComplete();
        _stopping.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _stopping.Dispose();
    }
}
