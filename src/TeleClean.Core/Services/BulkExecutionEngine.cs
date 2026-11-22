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
    private bool _isRunning;
    private CancellationTokenSource? _stopSignal;
    private int _processed;
    private int _total;
    private string _title = string.Empty;
    private int _succeeded;
    private int _failed;

    private void Publish(int seconds = 0) => ProgressChanged?.Invoke(this,
        new BulkProgressReport(_processed, _total, _title, State, seconds, _succeeded, _failed));

    private async Task PublishResultAsync(BulkLogEntry entry)
    {
        LogEntryAdded?.Invoke(this, entry);
        try { await _cacheService.AppendHistoryAsync(entry); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogEntryAdded?.Invoke(this, new BulkLogEntry(DateTime.UtcNow, 0, string.Empty,
                LeaveResult.Error, "Не удалось сохранить запись в локальном журнале."));
        }
    }

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

        ArgumentOutOfRangeException.ThrowIfLessThan(delayBetween, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(rateLimitDelay, TimeSpan.Zero);
        var list = items.Where(item => item.IsJoined).DistinctBy(item => (item.DialogType, item.Id)).ToList();
        lock (_stateLock)
        {
            if (_isRunning)
                throw new InvalidOperationException("A bulk operation is already in progress.");

            _stopSignal = new CancellationTokenSource();
            _isRunning = true;
            _state = BulkOperationState.Running;
            _isStopped = false;
            _isPaused = false;
        }

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopSignal.Token);
        var waitToken = waitCancellation.Token;
        _total = list.Count;
        _processed = _succeeded = _failed = 0;
        _title = string.Empty;
        bool completed = false;

        try
        {
            for (int i = 0; i < _total; i++)
            {
                while (_isPaused && !_isStopped)
                    await Task.Delay(100, waitToken);
                if (_isStopped) break;
                ct.ThrowIfCancellationRequested();

                var item = list[i];
                _title = item.Title;
                Publish();
                if (_isStopped) break;
                ct.ThrowIfCancellationRequested();

                string action = item.DialogType switch
                {
                    DialogType.Bot or DialogType.User or DialogType.Deleted => "Удаление диалога",
                    DialogType.Supergroup or DialogType.Group => "Выход из чата",
                    _ => "Выход из канала"
                };
                var result = await _telegramService.LeaveDialogAsync(item, ct);
                _processed++;
                if (result == LeaveResult.Success)
                {
                    _succeeded++;
                    item.StatusBadge = item.DialogType is DialogType.Bot or DialogType.User or DialogType.Deleted
                        ? "Удалён" : "✅ Вышел";
                    item.IsJoined = false;
                }
                else
                {
                    _failed++;
                    item.StatusBadge = result switch
                    {
                        LeaveResult.RateLimited => "⏳ Ограничение",
                        LeaveResult.Forbidden => "Нет доступа",
                        LeaveResult.NotFound => "Не найден",
                        _ => "Ошибка"
                    };
                }

                await PublishResultAsync(new BulkLogEntry(DateTime.UtcNow, item.Id, item.Title,
                    result, $"{item.StatusBadge}: {item.Title}", action) { DialogType = item.DialogType });

                if (result == LeaveResult.RateLimited)
                {
                    State = BulkOperationState.RateLimited;
                    var retryAfter = _telegramService.RateLimitRetryAfter ?? TimeSpan.Zero;
                    var wait = Math.Max(rateLimitDelay.TotalSeconds, retryAfter.TotalSeconds);
                    for (int seconds = (int)Math.Max(1, wait); seconds > 0 && !_isStopped; seconds--)
                    {
                        Publish(seconds);
                        await Task.Delay(1000, waitToken);
                    }

                    if (!_isStopped) State = _isPaused ? BulkOperationState.Paused : BulkOperationState.Running;
                }
                else
                {
                    Publish();
                    if (i < _total - 1 && !_isStopped)
                        await Task.Delay(delayBetween, waitToken);
                }
            }

            completed = !_isStopped && !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (_isStopped || ct.IsCancellationRequested)
        {
            // Record the completed prefix even when the user stops during a wait.
        }
        finally
        {
            try
            {
                State = completed ? BulkOperationState.Completed : BulkOperationState.Interrupted;
                _title = string.Empty;
                if (_total > 0)
                {
                    var summary = completed ? "Операция завершена" : "Операция прервана";
                    LogEntryAdded?.Invoke(this, new BulkLogEntry(DateTime.UtcNow, 0, string.Empty,
                        completed ? LeaveResult.Success : LeaveResult.Error,
                        $"{summary}. Успешно: {_succeeded}, ошибок: {_failed}"));
                }
                Publish();
            }
            finally
            {
                lock (_stateLock)
                {
                    _isRunning = false;
                    _stopSignal.Dispose();
                    _stopSignal = null;
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
                _state = BulkOperationState.Paused;

            }
            else return;
        }
        Publish();
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            if (_state != BulkOperationState.Paused) return;
            _isPaused = false;
            _state = BulkOperationState.Running;
        }
        Publish();
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!_isRunning) return;
            _isStopped = true;
            _stopSignal?.Cancel();
            _isPaused = false;
        }
    }
}
