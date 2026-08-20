using System.Diagnostics;
using System.Threading.Channels;

namespace Soltex.Whisper.Windows;

internal enum WindowsBoundedOperationFailure
{
    None,
    TimedOut,
    ProviderUnavailable
}

internal readonly record struct WindowsBoundedOperationResult<T>(
    bool Succeeded,
    T? Value,
    WindowsBoundedOperationFailure Failure);

/// <summary>
/// Executes potentially hostile cross-process COM work on a background MTA thread.
/// One total deadline covers waiting for the single permit and the operation itself.
/// If a provider remains stuck, it retains the only permit so later calls fail
/// closed instead of creating more native workers.
/// </summary>
internal sealed class WindowsBoundedMtaOperationHost
{
    private readonly TimeSpan _timeout;
    private readonly Channel<bool> _permit = CreatePermit();

    internal WindowsBoundedMtaOperationHost(TimeSpan timeout)
    {
        if (timeout < TimeSpan.FromMilliseconds(10) || timeout > TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Windows operation timeouts must be between 10 milliseconds and 5 seconds.");
        }

        _timeout = timeout;
    }

    internal async ValueTask<WindowsBoundedOperationResult<T>> RunAsync<T>(
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Stopwatch deadline = Stopwatch.StartNew();
        using CancellationTokenSource permitDeadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        permitDeadline.CancelAfter(_timeout);
        try
        {
            _ = await _permit.Reader
                .ReadAsync(permitDeadline.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed<T>(WindowsBoundedOperationFailure.TimedOut);
        }

        Task<T>? work = null;
        bool releaseDeferred = false;
        try
        {
            TimeSpan remaining = _timeout - deadline.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return Failed<T>(WindowsBoundedOperationFailure.TimedOut);
            }

            work = Start(operation);
            T value = await work
                .WaitAsync(remaining, cancellationToken)
                .ConfigureAwait(false);
            return new WindowsBoundedOperationResult<T>(
                Succeeded: true,
                value,
                WindowsBoundedOperationFailure.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (work is not null && !work.IsCompleted)
            {
                releaseDeferred = true;
                _ = ReleaseWhenCompleteAsync(work);
            }

            throw;
        }
        catch (TimeoutException)
        {
            if (work is not null && !work.IsCompleted)
            {
                releaseDeferred = true;
                _ = ReleaseWhenCompleteAsync(work);
            }

            return Failed<T>(WindowsBoundedOperationFailure.TimedOut);
        }
        catch (Exception exception) when (IsRecoverableProviderFailure(exception))
        {
            return Failed<T>(WindowsBoundedOperationFailure.ProviderUnavailable);
        }
        finally
        {
            if (!releaseDeferred)
            {
                ReleasePermit();
            }
        }
    }

    private static Task<T> Start<T>(Func<T> operation)
    {
        TaskCompletionSource<T> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Thread worker = new(() =>
        {
            try
            {
                completion.TrySetResult(operation());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Soltex Whisper Windows operation"
        };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        return completion.Task;
    }

    private async Task ReleaseWhenCompleteAsync(Task work)
    {
        try
        {
            await work.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Observe the eventual provider failure after the caller has already
            // received a content-free timeout or cancellation result.
        }
        finally
        {
            ReleasePermit();
        }
    }

    private void ReleasePermit()
    {
        if (!_permit.Writer.TryWrite(true))
        {
            throw new InvalidOperationException("The Windows operation permit was released twice.");
        }
    }

    private static Channel<bool> CreatePermit()
    {
        Channel<bool> permit = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        if (!permit.Writer.TryWrite(true))
        {
            throw new InvalidOperationException("The Windows operation permit could not be initialized.");
        }

        return permit;
    }

    private static WindowsBoundedOperationResult<T> Failed<T>(
        WindowsBoundedOperationFailure failure) => new(
        Succeeded: false,
        Value: default,
        failure);

    private static bool IsRecoverableProviderFailure(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;
}
