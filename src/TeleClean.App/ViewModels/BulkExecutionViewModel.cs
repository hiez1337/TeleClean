using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TeleClean.App.Messages;
using TeleClean.App.Services;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;
using Windows.ApplicationModel.DataTransfer;

namespace TeleClean.App.ViewModels;

public sealed partial class BulkExecutionViewModel : ViewModelBase,
    IRecipient<StartBulkExecutionMessage>
{
    private readonly IBulkExecutionEngine _engine;
    private readonly IConfigService _configService;
    private CancellationTokenSource? _executionCts;

    private readonly System.Diagnostics.Stopwatch _stopwatch = new();
    private System.Threading.Timer? _elapsedTimer;

    [ObservableProperty]
    private string _elapsedTimeText = "00:00:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationsHeaderText))]
    private int _activeOperationsCount = 0;

    public string OperationsHeaderText => $"Операции ({ActiveOperationsCount})";

    public ObservableCollection<BulkLogEntry> LogEntries { get; } = new();
    public ObservableCollection<BulkLogEntry> Logs => LogEntries;

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _currentItemTitle = "Ожидание запуска...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentageText))]
    private double _progressPercentage;

    public string ProgressPercentageText => $"{(int)ProgressPercentage}%";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingOnRateLimit))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    private BulkOperationState _currentState = BulkOperationState.Idle;

    [ObservableProperty]
    private int _secondsRemaining;

    [ObservableProperty]
    private int _successCount;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private string _currentStatusDescription = "Операция готова к запуску";

    [ObservableProperty]
    private string _stateBadgeText = "Ожидание";

    [ObservableProperty]
    private string _progressStepText = "Обработано 0 из 0 (0%)";

    [ObservableProperty]
    private string _successErrorCounterText = "Успешно: 0 | Ошибок: 0";

    public bool IsWaitingOnRateLimit => CurrentState == BulkOperationState.RateLimited;
    public bool CanPause => CurrentState == BulkOperationState.Running;
    public bool CanResume => CurrentState == BulkOperationState.Paused;
    public bool CanTogglePause => CanPause || CanResume;
    public bool CanStop => CurrentState is BulkOperationState.Running or BulkOperationState.Paused or BulkOperationState.RateLimited;
    public string PauseButtonText => CurrentState == BulkOperationState.Paused ? "▶ Продолжить" : "⏸ Пауза";

    public BulkExecutionViewModel(
        IBulkExecutionEngine engine,
        IConfigService configService,
        IDispatcherService dispatcher)
        : base(dispatcher)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        _engine.ProgressChanged += OnEngineProgressChanged;
        _engine.LogEntryAdded += OnEngineLogEntryAdded;

        WeakReferenceMessenger.Default.Register(this);
    }

    public void Receive(StartBulkExecutionMessage message)
    {
        if (message.Value != null && message.Value.Count > 0)
        {
            _ = StartExecutionAsync(message.Value);
        }
    }

    public async Task StartExecutionAsync(IReadOnlyList<DialogItem> items)
    {
        _engine.Stop();
        _executionCts?.Cancel();
        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        var ct = _executionCts.Token;

        LogEntries.Clear();
        OnPropertyChanged(nameof(Logs));
        CurrentIndex = 0;
        TotalCount = items.Count;
        ActiveOperationsCount = items.Count;
        SuccessCount = 0;
        ErrorCount = 0;
        ProgressPercentage = 0;
        CurrentState = BulkOperationState.Running;
        StateBadgeText = "Выполняется";
        CurrentStatusDescription = $"Запуск операции для {TotalCount} диалогов...";
        CurrentItemTitle = items.Count > 0 ? items[0].Title : "Запуск...";

        _stopwatch.Restart();
        _elapsedTimer?.Dispose();
        _elapsedTimer = new System.Threading.Timer(_ =>
        {
            Dispatcher.Enqueue(() =>
            {
                if (_stopwatch.IsRunning)
                {
                    ElapsedTimeText = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                }
            });
        }, null, 0, 500);

        var config = await _configService.LoadConfigAsync();
        var delay = TimeSpan.FromSeconds(config.DefaultDelaySeconds > 0 ? config.DefaultDelaySeconds : 3.0);
        var rateLimitDelay = TimeSpan.FromSeconds(30);

        _ = Task.Run(async () =>
        {
            try
            {
                await _engine.StartAsync(items, delay, rateLimitDelay, ct);
            }
            catch (OperationCanceledException)
            {
                // Clean user stop
            }
            catch (Exception ex)
            {
                Dispatcher.Enqueue(() =>
                {
                    ErrorMessage = $"Ошибка выполнения операции: {ex.Message}";
                });
            }
        }, ct);
    }

    private void OnEngineProgressChanged(object? sender, BulkProgressReport report)
    {
        Dispatcher.Enqueue(() =>
        {
            CurrentIndex = report.CurrentIndex;
            TotalCount = report.TotalCount;
            CurrentItemTitle = report.CurrentTitle;
            CurrentState = report.State;
            SecondsRemaining = report.SecondsRemaining;
            SuccessCount = report.SuccessCount;
            ErrorCount = report.ErrorCount;
            ProgressPercentage = report.Percent;

            ProgressStepText = $"Обработано {CurrentIndex} из {TotalCount} ({(int)ProgressPercentage}%)";
            SuccessErrorCounterText = $"Успешно: {SuccessCount} | Ошибок: {ErrorCount}";

            if (report.State is BulkOperationState.Completed or BulkOperationState.Interrupted)
            {
                ActiveOperationsCount = 0;
                _stopwatch.Stop();
                _elapsedTimer?.Dispose();
                _elapsedTimer = null;
                ElapsedTimeText = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
            }
            else
            {
                ActiveOperationsCount = Math.Max(0, TotalCount - CurrentIndex);
            }

            switch (CurrentState)
            {
                case BulkOperationState.Running:
                    CurrentStatusDescription = $"Обработка: {CurrentItemTitle}";
                    StateBadgeText = "Выполняется";
                    break;

                case BulkOperationState.Paused:
                    CurrentStatusDescription = "Операция приостановлена пользователем";
                    StateBadgeText = "На паузе";
                    break;

                case BulkOperationState.RateLimited:
                    CurrentStatusDescription = $"Ограничение Telegram (FloodWait). Ожидание {SecondsRemaining} сек...";
                    StateBadgeText = $"FloodWait ({SecondsRemaining}с)";
                    break;

                case BulkOperationState.Completed:
                    CurrentStatusDescription = $"Операция успешно завершена. Успешно: {SuccessCount}, ошибок: {ErrorCount}";
                    StateBadgeText = "Завершено";
                    break;

                case BulkOperationState.Interrupted:
                    CurrentStatusDescription = $"Операция прервана. Обработано {CurrentIndex} из {TotalCount}";
                    StateBadgeText = "Прервано";
                    break;

                default:
                    StateBadgeText = "Ожидание";
                    break;
            }
        });
    }

    private void OnEngineLogEntryAdded(object? sender, BulkLogEntry log)
    {
        Dispatcher.Enqueue(() =>
        {
            LogEntries.Add(log);
            OnPropertyChanged(nameof(Logs));
            if (LogEntries.Count > 1000)
            {
                LogEntries.RemoveAt(0);
            }
        });
    }

    [RelayCommand]
    public void TogglePause()
    {
        if (CurrentState == BulkOperationState.Paused)
        {
            _stopwatch.Start();
            _engine.Resume();
        }
        else if (CurrentState == BulkOperationState.Running)
        {
            _stopwatch.Stop();
            _engine.Pause();
        }
    }

    [RelayCommand]
    public void Stop()
    {
        _stopwatch.Stop();
        _elapsedTimer?.Dispose();
        _elapsedTimer = null;
        ActiveOperationsCount = 0;
        _engine.Stop();
        _executionCts?.Cancel();
    }

    [RelayCommand]
    public void CopyLog()
    {
        if (LogEntries.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var entry in LogEntries)
        {
            sb.AppendLine($"[{entry.Timestamp:HH:mm:ss}] {entry.Title} -> {entry.Result}: {entry.Message}");
        }

        try
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(sb.ToString());
            Clipboard.SetContent(dataPackage);
            StatusMessage = "Журнал операций скопирован в буфер обмена.";
        }
        catch
        {
            // Clipboard access might be denied in headless or background context
        }
    }

    public override void Dispose()
    {
        _engine.ProgressChanged -= OnEngineProgressChanged;
        _engine.LogEntryAdded -= OnEngineLogEntryAdded;
        _executionCts?.Cancel();
        _executionCts?.Dispose();
        WeakReferenceMessenger.Default.UnregisterAll(this);
        base.Dispose();
    }
}
