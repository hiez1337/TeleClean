using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace TeleClean.App.Services;

/// <summary>
/// Production implementation of IDispatcherService wrapping Microsoft.UI.Dispatching.DispatcherQueue.
/// </summary>
public sealed class WinUIDispatcherService : IDispatcherService
{
    private readonly DispatcherQueue _dispatcherQueue;

    public WinUIDispatcherService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    }

    public bool HasThreadAccess => _dispatcherQueue.HasThreadAccess;

    public void Enqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => action());
        }
    }

    public Task EnqueueAsync(Func<Task> function)
    {
        ArgumentNullException.ThrowIfNull(function);

        if (_dispatcherQueue.HasThreadAccess)
        {
            return function();
        }

        var tcs = new TaskCompletionSource();
        _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
        {
            try
            {
                await function();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}
