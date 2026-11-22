using System;
using System.Threading.Tasks;

namespace TeleClean.App.Services;

/// <summary>
/// Abstraction for marshalling callbacks and mutations to the UI thread.
/// Enables deterministic unit testing without WinUI 3 runtime initialization.
/// </summary>
public interface IDispatcherService
{
    /// <summary>
    /// Gets a value indicating whether the current thread has access to the UI thread.
    /// </summary>
    bool HasThreadAccess { get; }

    /// <summary>
    /// Enqueues or synchronously executes an action on the UI thread.
    /// </summary>
    void Enqueue(Action action);

    /// <summary>
    /// Enqueues or asynchronously executes an async function on the UI thread.
    /// </summary>
    Task EnqueueAsync(Func<Task> function);
}
