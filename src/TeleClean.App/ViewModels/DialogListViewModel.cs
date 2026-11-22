using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TeleClean.App.Messages;
using TeleClean.App.Services;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;

namespace TeleClean.App.ViewModels;

public enum DialogCategoryTab
{
    Channels = 0,
    Chats = 1,
    Bots = 2,
    Deleted = 3
}

public sealed partial class DialogListViewModel : ViewModelBase
{
    private readonly ITelegramService _telegramService;
    private readonly ICacheService _cacheService;
    private readonly IBulkExecutionEngine _engine;
    private readonly List<DialogItemViewModel> _allDialogs = new();
    private CancellationTokenSource? _avatarLoad;
    private bool _hasFreshDialogs;
    private bool _operationActive;

    private bool _showComparison;

    public bool ShowComparison
    {
        get => _showComparison;
        set
        {
            if (_showComparison == value) return;
            _showComparison = value;
            foreach (var item in _allDialogs) item.ShowComparison = value;
        }
    }

    public bool HasRequestedDialogs { get; private set; }
    public bool CanRefresh => !IsBusy && !_operationActive;
    public bool CanSelectVisible => CanSelect && VisibleDialogs.Any(dialog => dialog.IsJoined);
    public bool CanSelect => _hasFreshDialogs && CanRefresh;
    public bool HasFilters => !string.IsNullOrWhiteSpace(SearchQuery) || SelectedFilterIndex != 0;
    public bool ShowEmpty => !IsBusy && VisibleDialogs.Count == 0 && ErrorMessage == null;
    public string CategoryHint => CurrentCategory switch
    {
        DialogCategoryTab.Channels => "Подписки, из которых можно выйти.",
        DialogCategoryTab.Chats => "Группы и супергруппы, из которых можно выйти.",
        DialogCategoryTab.Bots => "Диалоги с ботами. Удаление истории не блокирует бота.",
        _ => "Диалоги с удалёнными аккаунтами."
    };
    public string EmptyTitle => HasFilters ? "Ничего не найдено" : "Здесь пока нет диалогов";
    public string EmptyHint => HasFilters ? "Измените запрос или сбросьте фильтры." : "Диалоги этой категории появятся после обновления списка.";


    [ObservableProperty]
    private IReadOnlyList<DialogItemViewModel> _visibleDialogs = Array.Empty<DialogItemViewModel>();

    public IReadOnlyList<string> FilterOptions { get; } = new[]
    {
        "Все",
        "Непрочитанные",
        "До 50 участников"
    };

    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "По названию (А-Я)",
        "По непрочитанным",
        "По числу участников",
        "По дате активности"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentCategoryTitleWithCount))]
    [NotifyPropertyChangedFor(nameof(CategoryHint))]
    [NotifyPropertyChangedFor(nameof(BottomStatusBarText))]
    private int _selectedCategoryIndex = 0;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _selectedFilterIndex = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleSortHeader))]
    [NotifyPropertyChangedFor(nameof(ParticipantsSortHeader))]
    [NotifyPropertyChangedFor(nameof(UnreadSortHeader))]
    [NotifyPropertyChangedFor(nameof(ActivitySortHeader))]
    private int _selectedSortIndex = 0;

    public string TitleSortHeader => SelectedSortIndex == 0 ? "Название ↑" : "Название";
    public string ParticipantsSortHeader => SelectedSortIndex == 2 ? "Участники ↓" : "Участники";
    public string UnreadSortHeader => SelectedSortIndex == 1 ? "Непрочитано ↓" : "Непрочитано";
    public string ActivitySortHeader => SelectedSortIndex == 3 ? "Активность ↓" : "Активность";

    [ObservableProperty]
    private string _selectionCounterText = "Выбрано: 0 из 0";

    [ObservableProperty]
    private string _actionButtonText = "Проверить выбор";

    [ObservableProperty]
    private bool _canExecuteAction;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private int _leftCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChannelsHeaderText))]
    [NotifyPropertyChangedFor(nameof(CurrentCategoryTitleWithCount))]
    private int _channelsCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChatsHeaderText))]
    [NotifyPropertyChangedFor(nameof(CurrentCategoryTitleWithCount))]
    private int _chatsCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BotsHeaderText))]
    [NotifyPropertyChangedFor(nameof(CurrentCategoryTitleWithCount))]
    private int _botsCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeletedHeaderText))]
    [NotifyPropertyChangedFor(nameof(CurrentCategoryTitleWithCount))]
    private int _deletedCount = 0;

    public string ChannelsHeaderText => $"Каналы ({ChannelsCount})";
    public string ChatsHeaderText => $"Чаты ({ChatsCount})";
    public string BotsHeaderText => $"Боты ({BotsCount})";
    public string DeletedHeaderText => $"Удалённые ({DeletedCount})";

    public string CurrentCategoryTitleWithCount => CurrentCategory switch
    {
        DialogCategoryTab.Channels => $"Каналы ({ChannelsCount})",
        DialogCategoryTab.Chats => $"Чаты ({ChatsCount})",
        DialogCategoryTab.Bots => $"Боты ({BotsCount})",
        DialogCategoryTab.Deleted => $"Удалённые ({DeletedCount})",
        _ => "Диалоги"
    };

    public string BottomStatusBarText
    {
        get
        {
            int total = VisibleDialogs.Count;
            string noun = CurrentCategory switch
            {
                DialogCategoryTab.Channels => GetRussianPlural(total, "канал", "канала", "каналов"),
                DialogCategoryTab.Chats => GetRussianPlural(total, "чат", "чата", "чатов"),
                DialogCategoryTab.Bots => GetRussianPlural(total, "бот", "бота", "ботов"),
                DialogCategoryTab.Deleted => GetRussianPlural(total, "аккаунт", "аккаунта", "аккаунтов"),
                _ => GetRussianPlural(total, "диалог", "диалога", "диалогов")
            };
            return $"{total} {noun} | Выбрано: {SelectedCount}";
        }
    }

    private bool _isUpdatingAllSelected;
    private bool _isBatchUpdating;
    public bool? IsAllSelectedInView
    {
        get
        {
            var joinable = VisibleDialogs.Where(d => d.IsJoined).ToList();
            if (joinable.Count == 0) return false;
            int selected = joinable.Count(d => d.IsSelected);
            if (selected == joinable.Count) return true;
            if (selected == 0) return false;
            return null;
        }
        set
        {
            if (!CanSelect || _isUpdatingAllSelected || _isBatchUpdating) return;
            if (!value.HasValue) return;
            _isUpdatingAllSelected = true;
            _isBatchUpdating = true;
            try
            {
                bool check = value.Value;
                foreach (var item in VisibleDialogs.Where(d => d.IsJoined))
                {
                    item.IsSelected = check;
                }
            }
            finally
            {
                _isBatchUpdating = false;
                _isUpdatingAllSelected = false;
            }
            UpdateCountersAndActionButton();
        }
    }

    public DialogCategoryTab CurrentCategory => (DialogCategoryTab)SelectedCategoryIndex;

    public DialogListViewModel(
        ITelegramService telegramService,
        ICacheService cacheService,
        IBulkExecutionEngine engine,
        IDispatcherService dispatcher)
        : base(dispatcher)
    {
        _telegramService = telegramService ?? throw new ArgumentNullException(nameof(telegramService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

        _engine.LogEntryAdded += OnEngineLogEntryAdded;
        _engine.ProgressChanged += OnEngineProgressChanged;
    }

    public DialogListViewModel(
        ITelegramService telegramService,
        ICacheService cacheService,
        IDispatcherService dispatcher)
        : this(telegramService, cacheService, new TeleClean.Core.Services.BulkExecutionEngine(telegramService, cacheService), dispatcher)
    {
    }

    private void OnEngineLogEntryAdded(object? sender, BulkLogEntry log)
    {
        if (log.Result != LeaveResult.Success || log.DialogId == 0) return;

        Dispatcher.Enqueue(() =>
        {
            var matchedVm = _allDialogs.FirstOrDefault(d => d.Id == log.DialogId && d.DialogType == log.DialogType);
            if (matchedVm != null)
            {
                string badge = matchedVm.DialogType is DialogType.Bot or DialogType.User or DialogType.Deleted ? "Удалён" : "Вышли";
                matchedVm.MarkLeft(badge);
                UpdateCountersAndActionButton();
            }
        });
    }

    private void OnEngineProgressChanged(object? sender, BulkProgressReport report)
    {
        Dispatcher.Enqueue(() =>
        {
            if (report.State is BulkOperationState.Completed or BulkOperationState.Interrupted) _ = PersistDialogsAsync();
        });
    }

    public void SetOperationActive(bool active)
    {
        _operationActive = active;
        foreach (var item in _allDialogs) item.SelectionEnabled = CanSelect;
        UpdateCountersAndActionButton();
    }

    public void ResetAccount()
    {
        _avatarLoad?.Cancel();
        HasRequestedDialogs = false;
        _hasFreshDialogs = false;
        SearchQuery = string.Empty;
        SelectedFilterIndex = 0;
        ErrorMessage = null;
        StatusMessage = null;
        SetDialogs(Array.Empty<DialogItem>());
    }

    private async Task PersistDialogsAsync()
    {
        try { await _cacheService.SaveDialogCacheAsync(_allDialogs.Select(d => d.Model.Clone()).ToArray()); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Не удалось сохранить локальную копию списка.";
        }
    }

    public Task InitializeAsync() => HasRequestedDialogs ? Task.CompletedTask : LoadDialogsAsync();

    [RelayCommand]
    public async Task LoadDialogsAsync()
    {
        if (!CanRefresh) return;
        IsBusy = true;
        foreach (var item in _allDialogs) item.SelectionEnabled = false;
        _hasFreshDialogs = false;
        _avatarLoad?.Cancel();
        ErrorMessage = null;
        StatusMessage = "Загрузка диалогов из Telegram…";
        UpdateCountersAndActionButton();

        try
        {
            if (!HasRequestedDialogs)
            {
                HasRequestedDialogs = true;
                SetDialogs(await _cacheService.LoadDialogCacheAsync());
            }

            var remote = await _telegramService.FetchDialogsAsync();
            SetDialogs(remote);
            _hasFreshDialogs = true;
            StatusMessage = "Список обновлён";
            await PersistDialogsAsync();

            _avatarLoad = new CancellationTokenSource();
            _ = FetchAvatarsInBackgroundAsync(_allDialogs.ToArray(), _avatarLoad);
        }
        catch
        {
            StatusMessage = VisibleDialogs.Count > 0 ? "Показана локальная копия. Для действий обновите список." : null;
            ErrorMessage = "Не удалось загрузить диалоги. Проверьте подключение и нажмите «Обновить».";
        }
        finally
        {
            IsBusy = false;
            foreach (var item in _allDialogs) item.SelectionEnabled = CanSelect;
            UpdateCountersAndActionButton();
        }
    }

    public void SetDialogs(IEnumerable<DialogItem> items)
    {
        foreach (var old in _allDialogs) old.SelectionChanged -= OnItemSelectionChanged;
        _allDialogs.Clear();
        foreach (var item in items)
        {
            var vm = new DialogItemViewModel(item);
            vm.SelectionChanged += OnItemSelectionChanged;
            vm.SelectionEnabled = CanSelect;
            vm.ShowComparison = ShowComparison;
            _allDialogs.Add(vm);
        }

        UpdateCategoryCounts();
        ApplyFiltersAndSort();
    }

    public void UpdateCategoryCounts()
    {
        ChannelsCount = _allDialogs.Count(d => d.DialogType == DialogType.Channel);
        ChatsCount = _allDialogs.Count(d => d.DialogType is DialogType.Supergroup or DialogType.Group);
        BotsCount = _allDialogs.Count(d => d.DialogType == DialogType.Bot);
        DeletedCount = _allDialogs.Count(d => d.DialogType == DialogType.Deleted);
        OnPropertyChanged(nameof(ChannelsHeaderText));
        OnPropertyChanged(nameof(ChatsHeaderText));
        OnPropertyChanged(nameof(BotsHeaderText));
        OnPropertyChanged(nameof(DeletedHeaderText));
        OnPropertyChanged(nameof(CurrentCategoryTitleWithCount));
        OnPropertyChanged(nameof(BottomStatusBarText));
    }

    private void OnItemSelectionChanged(DialogItemViewModel item)
    {
        if (_isBatchUpdating) return;
        UpdateCountersAndActionButton();
    }

    partial void OnSelectedCategoryIndexChanged(int value)
    {
        DeselectAll();
        ApplyFiltersAndSort();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFiltersAndSort();
    }

    partial void OnSelectedFilterIndexChanged(int value)
    {
        ApplyFiltersAndSort();
    }

    partial void OnSelectedSortIndexChanged(int value)
    {
        ApplyFiltersAndSort();
    }

    public void ApplyFiltersAndSort()
    {
        var category = CurrentCategory;

        // 1. Filter by category
        IEnumerable<DialogItemViewModel> query = category switch
        {
            DialogCategoryTab.Channels => _allDialogs.Where(d => d.DialogType == DialogType.Channel),
            DialogCategoryTab.Chats => _allDialogs.Where(d => d.DialogType is DialogType.Supergroup or DialogType.Group),
            DialogCategoryTab.Bots => _allDialogs.Where(d => d.DialogType == DialogType.Bot),
            DialogCategoryTab.Deleted => _allDialogs.Where(d => d.DialogType == DialogType.Deleted),
            _ => _allDialogs
        };

        // 2. Search query (title and username)
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim().TrimStart('@');
            query = query.Where(d =>
                d.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                (d.Username != null && d.Username.Contains(q, StringComparison.CurrentCultureIgnoreCase)));
        }

        // 3. Filter options
        query = SelectedFilterIndex switch
        {
            1 => query.Where(d => d.UnreadCount > 0),            // Непрочитанные
            2 => query.Where(d => d.ParticipantCount is > 0 and <= 50),     // Мало участников
            _ => query
        };

        // 4. Sorting
        query = SelectedSortIndex switch
        {
            1 => query.OrderByDescending(d => d.UnreadCount).ThenBy(d => d.Title),       // По активности
            2 => query.OrderByDescending(d => d.ParticipantCount).ThenBy(d => d.Title), // По участникам
            3 => query.OrderByDescending(d => d.LastActivity ?? DateTime.MinValue).ThenBy(d => d.Title), // По последней активности
            _ => query.OrderBy(d => d.Title, StringComparer.CurrentCultureIgnoreCase)   // По названию
        };

        var filtered = query.ToArray();
        var scope = filtered.ToHashSet();
        _isBatchUpdating = true;
        try
        {
            foreach (var item in _allDialogs)
                if (item.IsSelected && !scope.Contains(item)) item.IsSelected = false;
            VisibleDialogs = filtered;
        }
        finally { _isBatchUpdating = false; }

        UpdateCountersAndActionButton();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        SearchQuery = string.Empty;
        SelectedFilterIndex = 0;
    }

    [RelayCommand]
    public void SelectAll()
    {
        if (!CanSelect) return;
        _isBatchUpdating = true;
        try
        {
            foreach (var item in VisibleDialogs.Where(d => d.IsJoined))
            {
                item.IsSelected = true;
            }
        }
        finally
        {
            _isBatchUpdating = false;
        }
        UpdateCountersAndActionButton();
    }

    [RelayCommand]
    public void DeselectAll()
    {
        _isBatchUpdating = true;
        try
        {
            foreach (var item in VisibleDialogs)
            {
                item.IsSelected = false;
            }
        }
        finally
        {
            _isBatchUpdating = false;
        }
        UpdateCountersAndActionButton();
    }

    [RelayCommand]
    public void SortByTitle() => SelectedSortIndex = 0;

    [RelayCommand]
    public void SortByUnread() => SelectedSortIndex = 1;

    [RelayCommand]
    public void SortBySubscribers() => SelectedSortIndex = 2;

    [RelayCommand]
    public void SortByActivity() => SelectedSortIndex = 3;

    public void UpdateCountersAndActionButton()
    {
        int selectedInCurrentView = VisibleDialogs.Count(d => d.IsSelected && d.IsJoined);
        int totalInCurrentView = VisibleDialogs.Count;
        int leftInCurrentView = VisibleDialogs.Count(d => !d.IsJoined);

        SelectedCount = selectedInCurrentView;
        LeftCount = leftInCurrentView;
        CanExecuteAction = CanSelect && selectedInCurrentView > 0;
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(CanSelectVisible));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));

        SelectionCounterText = $"Выбрано: {selectedInCurrentView} из {totalInCurrentView}" +
            (leftInCurrentView > 0 ? $" · Обработано: {leftInCurrentView}" : "");

        ActionButtonText = selectedInCurrentView > 0
            ? $"Проверить выбор ({selectedInCurrentView})"
            : "Проверить выбор";

        OnPropertyChanged(nameof(BottomStatusBarText));
        OnPropertyChanged(nameof(CurrentCategoryTitleWithCount));

        _isUpdatingAllSelected = true;
        try
        {
            OnPropertyChanged(nameof(IsAllSelectedInView));
        }
        finally
        {
            _isUpdatingAllSelected = false;
        }
    }

    public IReadOnlyList<DialogItem> GetSelectedItems() => CanExecuteAction
        ? VisibleDialogs.Where(d => d.IsSelected && d.IsJoined).Select(d => d.Model).ToArray()
        : Array.Empty<DialogItem>();

    public void ExecuteSelection(IReadOnlyList<DialogItem> items)
    {
        if (!CanExecuteAction || items.Count == 0) return;
        _avatarLoad?.Cancel();
        WeakReferenceMessenger.Default.Send(new StartBulkExecutionMessage(items));
    }

    private async Task FetchAvatarsInBackgroundAsync(DialogItemViewModel[] items, CancellationTokenSource source)
    {
        var ct = source.Token;
        try
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(item.AvatarLocalPath))
                {
                    var path = await _telegramService.DownloadAvatarAsync(item.Model, ct);
                    if (!ct.IsCancellationRequested && path != null) item.AvatarLocalPath = path;
                }
                // Yield even on cache hits so keyboard and pointer input can run.
                await Task.Yield();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (System.IO.IOException) { }
        catch (UnauthorizedAccessException) { }
        finally
        {
            if (ReferenceEquals(_avatarLoad, source)) _avatarLoad = null;
            source.Dispose();
        }
    }

    public static string GetRussianPlural(int count, string one, string two, string five)
    {
        long n = Math.Abs((long)count) % 100;
        long n1 = n % 10;
        if (n is > 10 and < 20) return five;
        if (n1 is > 1 and < 5) return two;
        if (n1 == 1) return one;
        return five;
    }

    public override void Dispose()
    {
        _avatarLoad?.Cancel();
        _engine.LogEntryAdded -= OnEngineLogEntryAdded;
        _engine.ProgressChanged -= OnEngineProgressChanged;
        base.Dispose();
    }
}
