using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;

namespace TeleClean.Core.Services;

/// <summary>
/// Production implementation of IBulkExecutionEngine managing the sequential bulk-leave loop,
/// pause/resume/stop controls, flood-wait backoff, and event notifications.
/// </summary>
public sealed class BulkExecutionEngine : IBulkExecutionEngine
{
    private readonly ITelegramService _telegramService;
    private readonly ICacheService _cacheService;
    private readonly object _stateLock = new();

    private BulkOperationState _state = BulkOperationState.Idle;
    private volatile bool _isStopped;
    private volatile bool _isPaused;
    private readonly ManualResetEventSlim _pauseEvent = new(true);

    public BulkOperationState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
        private set
        {
            lock (_stateLock)
            {
                _state = value;
            }
        }
    }

    public event EventHandler<BulkProgressReport>? ProgressChanged;
    public event EventHandler<BulkLogEntry>? LogEntryAdded;

    public BulkExecutionEngine(ITelegramService telegramService, ICacheService cacheService)
    {
        _telegramService = telegramService;
        _cacheService = cacheService;
    }

    public async Task StartAsync(
        IEnumerable<DialogItem> items,
        TimeSpan delayBetween,
        TimeSpan rateLimitDelay,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var list = items.ToList();
        if (list.Count == 0)
        {
            State = BulkOperationState.Completed;
            ProgressChanged?.Invoke(this, new BulkProgressReport(0, 0, string.Empty, BulkOperationState.Completed, 0));
            return;
        }

        lock (_stateLock)
        {
            if (_state is BulkOperationState.Running or BulkOperationState.Paused or BulkOperationState.RateLimited)
            {
                throw new InvalidOperationException("A bulk operation is already in progress.");
            }

            _state = BulkOperationState.Running;
            _isStopped = false;
            _isPaused = false;
            _pauseEvent.Set();
        }

        int total = list.Count;
        int successCount = 0;
        int errorCount = 0;

        try
        {
            for (int i = 0; i < total; i++)
            {
                // Check cancellation or stop signal
                if (_isStopped || ct.IsCancellationRequested)
                {
                    State = BulkOperationState.Interrupted;
                    var stopEntry = new BulkLogEntry(
                        DateTime.UtcNow, 0, string.Empty, LeaveResult.Error, "Операция прервана пользователем.");
                    LogEntryAdded?.Invoke(this, stopEntry);
                    break;
                }

                // Wait if paused
                while (_isPaused && !_isStopped && !ct.IsCancellationRequested)
                {
                    await Task.Delay(150, ct);
                }

                if (_isStopped || ct.IsCancellationRequested)
                {
                    State = BulkOperationState.Interrupted;
                    break;
                }

                var item = list[i];
                int estimatedRemaining = (total - i) * (int)Math.Max(1, delayBetween.TotalSeconds);

                // Report progress for current item
                ProgressChanged?.Invoke(this, new BulkProgressReport(
                    currentIndex: i + 1,
                    totalCount: total,
                    currentTitle: item.Title,
                    state: State,
                    secondsRemaining: estimatedRemaining,
                    successCount: successCount,
                    errorCount: errorCount
                ));

                // Execute leave/delete via TelegramService
                string actionName = item.DialogType switch
                {
                    DialogType.Deleted => "Удаление диалога",
                    DialogType.Bot => "Остановка бота",
                    DialogType.Supergroup or DialogType.Group => "Выход из чата",
                    _ => "Выход из канала"
                };

                var result = await _telegramService.LeaveDialogAsync(item, ct);

                if (result == LeaveResult.Success)
                {
                    successCount++;
                    item.StatusBadge = item.DialogType == DialogType.Deleted ? "🗑 Удален" : "✅ Вышел";
                    item.IsJoined = false;

                    var log = new BulkLogEntry(
                        DateTime.UtcNow, item.Id, item.Title, LeaveResult.Success, $"Успешно: {item.Title}", actionName);
                    LogEntryAdded?.Invoke(this, log);
                    await _cacheService.AppendHistoryAsync(log, ct);
                }
                else if (result == LeaveResult.RateLimited)
                {
                    errorCount++;
                    item.StatusBadge = "⏳ Ограничение";

                    State = BulkOperationState.RateLimited;
                    var log = new BulkLogEntry(
                        DateTime.UtcNow, item.Id, item.Title, LeaveResult.RateLimited,
                        $"⏳ Превышен лимит запросов (Rate Limit). Ожидание {(int)rateLimitDelay.TotalSeconds} сек...", actionName);
                    LogEntryAdded?.Invoke(this, log);
                    await _cacheService.AppendHistoryAsync(log, ct);

                    // Backoff loop
                    int waitSec = (int)Math.Max(1, rateLimitDelay.TotalSeconds);
                    for (int s = waitSec; s > 0; s--)
                    {
                        if (_isStopped || ct.IsCancellationRequested) break;
                        ProgressChanged?.Invoke(this, new BulkProgressReport(
                            currentIndex: i + 1,
                            totalCount: total,
                            currentTitle: item.Title,
                            state: BulkOperationState.RateLimited,
                            secondsRemaining: s,
                            successCount: successCount,
                            errorCount: errorCount
                        ));
                        await Task.Delay(1000, ct);
                    }

                    if (!_isStopped && !ct.IsCancellationRequested)
                    {
                        State = BulkOperationState.Running;
                    }
                }
                else
                {
                    errorCount++;
                    item.StatusBadge = result switch
                    {
                        LeaveResult.Forbidden => "⛔ Нет доступа",
                        LeaveResult.NotFound => "❓ Не найден",
                        _ => "❌ Ошибка"
                    };

                    var log = new BulkLogEntry(
                        DateTime.UtcNow, item.Id, item.Title, result,
                        $"{item.StatusBadge}: {item.Title}", actionName);
                    LogEntryAdded?.Invoke(this, log);
                    await _cacheService.AppendHistoryAsync(log, ct);
                }

                // Pacing delay between actions
                if (result == LeaveResult.Success && i < total - 1 && !_isStopped && !ct.IsCancellationRequested)
                {
                    int delaySeconds = (int)Math.Max(0, delayBetween.TotalSeconds);
                    for (int d = delaySeconds; d > 0; d--)
                    {
                        if (_isStopped || ct.IsCancellationRequested) break;
                        await Task.Delay(1000, ct);
                    }
                }
            }

            if (!_isStopped && !ct.IsCancellationRequested)
            {
                State = BulkOperationState.Completed;
                var summary = new BulkLogEntry(
                    DateTime.UtcNow, 0, string.Empty, LeaveResult.Success,
                    $"Операция завершена. Успешно: {successCount}, Ошибок: {errorCount}");
                LogEntryAdded?.Invoke(this, summary);

                ProgressChanged?.Invoke(this, new BulkProgressReport(
                    currentIndex: total,
                    totalCount: total,
                    currentTitle: string.Empty,
                    state: BulkOperationState.Completed,
                    secondsRemaining: 0,
                    successCount: successCount,
                    errorCount: errorCount
                ));
            }
        }
        catch (OperationCanceledException)
        {
            State = BulkOperationState.Interrupted;
            var stopEntry = new BulkLogEntry(
                DateTime.UtcNow, 0, string.Empty, LeaveResult.Error, "Операция отменена.");
            LogEntryAdded?.Invoke(this, stopEntry);
        }
        finally
        {
            lock (_stateLock)
            {
                if (_state != BulkOperationState.Interrupted && _state != BulkOperationState.Completed)
                {
                    _state = BulkOperationState.Idle;
                }
            }
        }
    }

    public void Pause()
    {
        lock (_stateLock)
        {
            if (_state == BulkOperationState.Running)
            {
                _isPaused = true;
                _pauseEvent.Reset();
                _state = BulkOperationState.Paused;

                var log = new BulkLogEntry(
                    DateTime.UtcNow, 0, string.Empty, LeaveResult.Success, "Операция приостановлена.");
                LogEntryAdded?.Invoke(this, log);
            }
        }
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            if (_state == BulkOperationState.Paused)
            {
                _isPaused = false;
                _pauseEvent.Set();
                _state = BulkOperationState.Running;

                var log = new BulkLogEntry(
                    DateTime.UtcNow, 0, string.Empty, LeaveResult.Success, "Операция возобновлена.");
                LogEntryAdded?.Invoke(this, log);
            }
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            _isStopped = true;
            _isPaused = false;
            _pauseEvent.Set();
            _state = BulkOperationState.Interrupted;
        }
    }
}
