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

    public ObservableCollection<DialogItemViewModel> VisibleDialogs { get; } = new();

    public IReadOnlyList<string> FilterOptions { get; } = new[]
    {
        "Все",
        "Непрочитанные",
        "Мало участников (≤50)"
    };

    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "По названию (А-Я)",
        "По активности (непрочитанные)",
        "По числу участников",
        "По последней активности"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentCategoryTitleWithCount))]
    [NotifyPropertyChangedFor(nameof(BottomStatusBarText))]
    private int _selectedCategoryIndex = 0;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _selectedFilterIndex = 0;

    [ObservableProperty]
    private int _selectedSortIndex = 0;

    [ObservableProperty]
    private string _selectionCounterText = "Выбрано: 0 из 0";

    [ObservableProperty]
    private string _actionButtonText = "Выйти из выбранных";

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
            if (_isUpdatingAllSelected || _isBatchUpdating) return;
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
            var matchedVm = _allDialogs.FirstOrDefault(d => d.Id == log.DialogId);
            if (matchedVm != null)
            {
                string badge = matchedVm.DialogType == DialogType.Deleted ? "🗑 Удален" : "✅ Вышел";
                matchedVm.MarkLeft(badge);
                UpdateCountersAndActionButton();
            }
        });
    }

    private void OnEngineProgressChanged(object? sender, BulkProgressReport report)
    {
        if (report.State is BulkOperationState.Completed or BulkOperationState.Interrupted)
        {
            var models = _allDialogs.Select(d => d.Model).ToList();
            _ = _cacheService.SaveDialogCacheAsync(models);
        }
    }

    public async Task InitializeAsync()
    {
        await LoadDialogsAsync();
    }

    [RelayCommand]
    public async Task LoadDialogsAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Загрузка списка диалогов...";

        try
        {
            // 1. Try disk cache first for instant startup
            var cached = await _cacheService.LoadDialogCacheAsync();
            if (cached != null && cached.Count > 0)
            {
                SetDialogs(cached);
                StatusMessage = $"Загружено {cached.Count} диалогов из кэша.";
            }

            // 2. Refresh from Telegram MTProto
            var remote = await _telegramService.FetchDialogsAsync();
            if (remote != null && remote.Count > 0)
            {
                SetDialogs(remote);
                await _cacheService.SaveDialogCacheAsync(remote);
                StatusMessage = $"Синхронизировано {remote.Count} диалогов.";

                // 3. Trigger asynchronous background avatar fetching
                _ = FetchAvatarsInBackgroundAsync(remote);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Не удалось загрузить диалоги: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetDialogs(IEnumerable<DialogItem> items)
    {
        _allDialogs.Clear();
        foreach (var item in items)
        {
            var vm = new DialogItemViewModel(item);
            vm.SelectionChanged += OnItemSelectionChanged;
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
            string q = SearchQuery.Trim();
            query = query.Where(d =>
                d.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                (d.Username != null && d.Username.Contains(q, StringComparison.CurrentCultureIgnoreCase)));
        }

        // 3. Filter options
        query = SelectedFilterIndex switch
        {
            1 => query.Where(d => d.UnreadCount > 0),            // Непрочитанные
            2 => query.Where(d => d.ParticipantCount <= 50),     // Мало участников
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

        var filteredList = query.ToList();

        VisibleDialogs.Clear();
        foreach (var item in filteredList)
        {
            VisibleDialogs.Add(item);
        }

        UpdateCountersAndActionButton();
    }

    [RelayCommand]
    public void SelectAll()
    {
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
        int selectedInCurrentView = VisibleDialogs.Count(d => d.IsSelected);
        int totalInCurrentView = VisibleDialogs.Count;
        int leftInCurrentView = VisibleDialogs.Count(d => !d.IsJoined);

        SelectedCount = selectedInCurrentView;
        LeftCount = leftInCurrentView;
        CanExecuteAction = selectedInCurrentView > 0;

        SelectionCounterText = $"Выбрано: {selectedInCurrentView} из {totalInCurrentView}" +
            (leftInCurrentView > 0 ? $" | Вышло: {leftInCurrentView}" : "");

        var category = CurrentCategory;
        if (selectedInCurrentView == 0)
        {
            ActionButtonText = category switch
            {
                DialogCategoryTab.Deleted => "🗑 Удалить выбранные",
                DialogCategoryTab.Bots => "🚪 Остановить выбранные",
                _ => "🚪 Выйти из выбранных"
            };
        }
        else
        {
            ActionButtonText = category switch
            {
                DialogCategoryTab.Deleted => $"🗑 Удалить {selectedInCurrentView} {GetRussianPlural(selectedInCurrentView, "диалог", "диалога", "диалогов")}",
                DialogCategoryTab.Bots => $"🚪 Остановить {selectedInCurrentView} {GetRussianPlural(selectedInCurrentView, "бота", "ботов", "ботов")}",
                DialogCategoryTab.Chats => $"🚪 Выйти из {selectedInCurrentView} {GetRussianPlural(selectedInCurrentView, "чата", "чатов", "чатов")}",
                _ => $"🚪 Выйти из {selectedInCurrentView} {GetRussianPlural(selectedInCurrentView, "канала", "каналов", "каналов")}"
            };
        }

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

    [RelayCommand]
    public void StartBulkAction()
    {
        var selectedItems = VisibleDialogs
            .Where(d => d.IsSelected && d.IsJoined)
            .Select(d => d.Model)
            .ToList();

        if (selectedItems.Count == 0) return;

        WeakReferenceMessenger.Default.Send(new StartBulkExecutionMessage(selectedItems));
    }

    private async Task FetchAvatarsInBackgroundAsync(IReadOnlyList<DialogItem> items)
    {
        try
        {
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.AvatarLocalPath) || !System.IO.File.Exists(item.AvatarLocalPath))
                {
                    string? path = await _telegramService.DownloadAvatarAsync(item.Id);
                    if (!string.IsNullOrEmpty(path))
                    {
                        item.AvatarLocalPath = path;
                        var matchedVm = _allDialogs.FirstOrDefault(d => d.Id == item.Id);
                        if (matchedVm != null)
                        {
                            Dispatcher.Enqueue(() => matchedVm.AvatarLocalPath = path);
                        }
                    }
                }
            }
        }
        catch
        {
            // Silently ignore background avatar fetch faults
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
        _engine.LogEntryAdded -= OnEngineLogEntryAdded;
        _engine.ProgressChanged -= OnEngineProgressChanged;
        base.Dispose();
    }
}
