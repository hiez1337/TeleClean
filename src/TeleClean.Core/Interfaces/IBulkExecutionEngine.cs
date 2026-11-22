using TeleClean.Core.Models;

namespace TeleClean.Core.Interfaces;

/// <summary>
/// Engine coordinating sequential bulk leaving and deletion with rate-limiting backoff and pause/resume controls.
/// </summary>
public interface IBulkExecutionEngine
{
    /// <summary>
    /// Current execution state of the engine.
    /// </summary>
    BulkOperationState State { get; }

    /// <summary>
    /// Emitted whenever progress changes (current item, index, remaining seconds).
    /// </summary>
    event EventHandler<BulkProgressReport> ProgressChanged;

    /// <summary>
    /// Emitted whenever a log entry is recorded for an item or error.
    /// </summary>
    event EventHandler<BulkLogEntry> LogEntryAdded;

    /// <summary>
    /// Starts sequential bulk leave/delete operation over the specified dialog items.
    /// </summary>
    /// <param name="items">The items to leave or delete.</param>
    /// <param name="delayBetween">Pacing delay between successful requests (default 3s).</param>
    /// <param name="rateLimitDelay">Backoff delay on rate limits (default 30s or dynamic).</param>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    Task StartAsync(IEnumerable<DialogItem> items, TimeSpan delayBetween, TimeSpan rateLimitDelay, CancellationToken ct = default);

    /// <summary>
    /// Pauses execution loop after the current request finishes.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes execution loop from paused state.
    /// </summary>
    void Resume();

    /// <summary>
    /// Stops and interrupts execution immediately.
    /// </summary>
    void Stop();
}
