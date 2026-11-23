using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TeleClean.App.Helpers;
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
    private CancellationTokenSource? _delayDebounceCts;
    private bool _isInitializing;

    [ObservableProperty]
    private int _selectedThemeIndex = 0; // 0: System, 1: Light, 2: Dark

    [ObservableProperty]
    private int _selectedBackdropIndex = 0; // 0: Mica, 1: Mica Alt, 2: Acrylic, 3: Solid

    [ObservableProperty]
    private double _apiId;

    [ObservableProperty]
    private string _apiHash = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DelaySecondsText))]
    private double _delayBetweenRequests = 3.0;

    [ObservableProperty]
    private string _proxyUrl = string.Empty;

    [ObservableProperty]
    private bool _hasProxyConfigured;

    public string DelaySecondsText => $"{DelayBetweenRequests:F1} сек";
    public string AppVersionText => $"TeleClean {typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "3.1.0"}";
    public string DataDirectory => _configService.GetDataDirectory();
    public string SessionStatusText => _telegramService.IsAuthorized ? "Сессия: Активна" : "Сессия: Не авторизован";
    public string SessionDescriptionText => _telegramService.IsAuthorized
        ? "Сессия активна. Выход завершит текущую сессию TeleClean на этом устройстве."
        : "Авторизация не активна.";
    public string SettingsFooterText => $"{AppVersionText} | Хранилище: {_configService.GetDataDirectory()} | {(_telegramService.IsAuthorized ? "Сессия: Активна" : "Сессия: Требуется вход")}";

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
                "Solid" => 3,
                _ => 0
            };

            var (effId, effHash) = _configService.GetEffectiveCredentials();
            ApiId = effId;
            ApiHash = effHash;
            DelayBetweenRequests = _config.DefaultDelaySeconds > 0 ? _config.DefaultDelaySeconds : 3.0;
            ProxyUrl = _config.ProxyUrl ?? string.Empty;
            HasProxyConfigured = !string.IsNullOrWhiteSpace(_config.ProxyUrl);

            OnPropertyChanged(nameof(SessionStatusText));
            OnPropertyChanged(nameof(SessionDescriptionText));
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
            3 => "Solid",
            _ => "Mica"
        };

        _config.Backdrop = backdropName;
        _ = SavePreferencesAsync();

        WeakReferenceMessenger.Default.Send(new BackdropChangedMessage(backdropName));
    }

    partial void OnDelayBetweenRequestsChanged(double value)
    {
        if (_isInitializing) return;
        _config.DefaultDelaySeconds = Math.Clamp(Math.Round(value, 1), 1.0, 10.0);

        _delayDebounceCts?.Cancel();
        _delayDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _delayDebounceCts = cts;
        _ = DebouncedSavePreferencesAsync(cts);
    }

    private async Task DebouncedSavePreferencesAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(250, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                await SavePreferencesAsync(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer slider movement.
        }
    }

    private async Task SavePreferencesAsync(CancellationToken ct = default)
    {
        try { await _configService.SaveConfigAsync(_config, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch
        {
            ErrorMessage = "Не удалось сохранить настройки. Проверьте доступ к папке данных.";
        }
    }

    [RelayCommand]
    public async Task SaveApiKeysAsync()
    {
        if (!double.IsFinite(ApiId))
        {
            ErrorMessage = "Введите корректные API ID и API Hash.";
            return;
        }

        int id = (int)ApiId;
        string trimmedHash = ApiHash.Trim();
        if (id <= 0 || id != ApiId || trimmedHash.Length != 32 || !trimmedHash.All(Uri.IsHexDigit))
        {
            ErrorMessage = "Введите корректные API ID и API Hash.";
            return;
        }

        try
        {
            _config.ApiId = id;
            _config.ApiHash = trimmedHash;
            _config.ApiKeys = new ApiKeysConfig
            {
                ApiId = id,
                ApiHash = trimmedHash
            };

            await _configService.SaveConfigAsync(_config);
            StatusMessage = "API ключи сохранены. Изменения вступят в силу при перезапуске.";
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
        if (!ProxyUrlValidator.TryValidateAndNormalize(ProxyUrl, out string? normalized, out string? validationError))
        {
            ErrorMessage = validationError;
            StatusMessage = null;
            return;
        }

        try
        {
            _config.ProxyUrl = normalized;
            ProxyUrl = normalized ?? string.Empty;
            HasProxyConfigured = normalized != null;
            await _configService.SaveConfigAsync(_config);
            StatusMessage = normalized == null
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
    public void OpenDataDirectory()
    {
        try
        {
            string dir = DataDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch
        {
            ErrorMessage = "Не удалось открыть папку данных в Проводнике.";
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
            OnPropertyChanged(nameof(SessionStatusText));
            OnPropertyChanged(nameof(SessionDescriptionText));
            OnPropertyChanged(nameof(SettingsFooterText));

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

    public override void Dispose()
    {
        _delayDebounceCts?.Cancel();
        _delayDebounceCts?.Dispose();
        _delayDebounceCts = null;
        base.Dispose();
    }
}
