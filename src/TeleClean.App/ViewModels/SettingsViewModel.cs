using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TeleClean.App.Messages;
using TeleClean.App.Services;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;

namespace TeleClean.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly ICacheService _cacheService;
    private readonly ITelegramService _telegramService;
    private AppConfig _config = new();

    [ObservableProperty]
    private int _selectedThemeIndex = 0; // 0: System, 1: Light, 2: Dark

    [ObservableProperty]
    private int _selectedBackdropIndex = 0; // 0: Mica, 1: Mica Alt, 2: Acrylic

    [ObservableProperty]
    private double _apiId;

    [ObservableProperty]
    private string _apiHash = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DelaySecondsText))]
    private double _delayBetweenRequests = 3.0;

    [ObservableProperty]
    private string _proxyUrl = string.Empty;

    private bool _isInitializing;

    public string DelaySecondsText => $"{DelayBetweenRequests:F1} сек";
    public string AppVersionText => "TeleClean v2.0.0";
    public string SessionStatusText => _telegramService.IsAuthorized ? "Сессия: Активна" : "Сессия: Не авторизован";
    public string SettingsFooterText => $"TeleClean v2.0.0 | Хранилище: {_configService.GetDataDirectory()} | {(_telegramService.IsAuthorized ? "Сессия: Активна" : "Сессия: Требуется вход")}";

    public SettingsViewModel(
        IConfigService configService,
        ICacheService cacheService,
        ITelegramService telegramService,
        IDispatcherService dispatcher)
        : base(dispatcher)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _telegramService = telegramService ?? throw new ArgumentNullException(nameof(telegramService));
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        _isInitializing = true;
        try
        {
            _config = await _configService.LoadConfigAsync();

            SelectedThemeIndex = _config.Theme?.ToLowerInvariant() switch
            {
                "light" => 1,
                "dark" => 2,
                _ => 0
            };

            SelectedBackdropIndex = _config.Backdrop switch
            {
                "MicaAlt" => 1,
                "Acrylic" => 2,
                _ => 0
            };

            var (effId, effHash) = _configService.GetEffectiveCredentials();
            ApiId = effId;
            ApiHash = effHash;
            DelayBetweenRequests = _config.DefaultDelaySeconds > 0 ? _config.DefaultDelaySeconds : 3.0;
            ProxyUrl = _config.ProxyUrl ?? string.Empty;

            OnPropertyChanged(nameof(SessionStatusText));
            OnPropertyChanged(nameof(SettingsFooterText));
        }
        catch
        {
            ErrorMessage = "Не удалось загрузить настройки. Повторите попытку.";
        }
        finally
        {
            _isInitializing = false;
            IsBusy = false;
        }
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializing) return;

        string themeName = value switch
        {
            1 => "light",
            2 => "dark",
            _ => "system"
        };

        _config.Theme = themeName;
        _ = SavePreferencesAsync();

        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(themeName));
    }

    partial void OnSelectedBackdropIndexChanged(int value)
    {
        if (_isInitializing) return;

        string backdropName = value switch
        {
            1 => "MicaAlt",
            2 => "Acrylic",
            _ => "Mica"
        };

        _config.Backdrop = backdropName;
        _ = SavePreferencesAsync();

        WeakReferenceMessenger.Default.Send(new BackdropChangedMessage(backdropName));
    }

    partial void OnDelayBetweenRequestsChanged(double value)
    {
        if (_isInitializing) return;
        _config.DefaultDelaySeconds = Math.Round(value, 1);
        _ = SavePreferencesAsync();
    }

    private async Task SavePreferencesAsync()
    {
        try { await _configService.SaveConfigAsync(_config); }
        catch
        {
            ErrorMessage = "Не удалось сохранить настройки. Проверьте доступ к папке данных.";
        }
    }

    [RelayCommand]
    public async Task SaveApiKeysAsync()
    {
        int id = (int)ApiId;
        if (id <= 0 || id != ApiId || ApiHash.Trim().Length != 32 || !System.Linq.Enumerable.All(ApiHash.Trim(), Uri.IsHexDigit))
        {
            ErrorMessage = "Введите корректные API ID и API Hash.";
            return;
        }

        try
        {
            _config.ApiId = id;
            _config.ApiHash = ApiHash.Trim();
            _config.ApiKeys = new ApiKeysConfig
            {
                ApiId = id,
                ApiHash = ApiHash.Trim()
            };

            await _configService.SaveConfigAsync(_config);
            StatusMessage = "API ключи успешно сохранены.";
            ErrorMessage = null;
        }
        catch
        {
            ErrorMessage = "Ошибка сохранения API ключей. Повторите попытку.";
        }
    }

    [RelayCommand]
    public async Task SaveProxyAsync()
    {
        try
        {
            _config.ProxyUrl = string.IsNullOrWhiteSpace(ProxyUrl) ? null : ProxyUrl.Trim();
            await _configService.SaveConfigAsync(_config);
            StatusMessage = string.IsNullOrWhiteSpace(_config.ProxyUrl)
                ? "Прокси отключён. Перезапустите приложение."
                : "Прокси сохранён. Перезапустите приложение для применения.";
            ErrorMessage = null;
        }
        catch
        {
            ErrorMessage = "Ошибка сохранения прокси. Повторите попытку.";
        }
    }

    [RelayCommand]
    public async Task ClearProxyAsync()
    {
        ProxyUrl = string.Empty;
        await SaveProxyAsync();
    }

    [RelayCommand]
    public async Task ClearAvatarCacheAsync()
    {
        try
        {
            await _cacheService.ClearAvatarCacheAsync();

            StatusMessage = "Кэш аватаров успешно очищен.";
            ErrorMessage = null;
        }
        catch
        {
            ErrorMessage = "Ошибка очистки кэша. Повторите попытку.";
        }
    }

    [RelayCommand]
    public async Task ResetSessionAsync()
    {
        IsBusy = true;
        try
        {
            await _telegramService.ResetSessionAsync();

            StatusMessage = "Сессия сброшена.";
            ErrorMessage = null;

            WeakReferenceMessenger.Default.Send(new AuthenticationStateChangedMessage(false));
        }
        catch
        {
            ErrorMessage = "Ошибка сброса сессии. Повторите попытку.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
