using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TeleClean.App.Helpers;
using TeleClean.App.Messages;
using TeleClean.App.Services;
using TeleClean.Core.Interfaces;

namespace TeleClean.App.ViewModels;

public enum AuthMode
{
    QrCode,
    SessionRecovery,
    PhoneInput,
    CodeInput,
    TwoFactor
}

public sealed partial class AuthViewModel : ViewModelBase,
    IRecipient<AuthenticationStateChangedMessage>
{
    private readonly ITelegramService _telegramService;
    private readonly IConfigService? _configService;
    private CancellationTokenSource? _qrCts;
    private int _isInitializing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecoveryMode))]
    [NotifyPropertyChangedFor(nameof(IsQrMode))]
    [NotifyPropertyChangedFor(nameof(IsPhoneMode))]
    [NotifyPropertyChangedFor(nameof(Is2FaMode))]
    [NotifyPropertyChangedFor(nameof(IsSendCodeButtonVisible))]
    [NotifyPropertyChangedFor(nameof(IsCodeInputVisible))]
    [NotifyPropertyChangedFor(nameof(IsPhoneInputEnabled))]
    private AuthMode _currentMode = AuthMode.QrCode;

    [ObservableProperty]
    private string _phoneNumber = string.Empty;

    [ObservableProperty]
    private string _phoneCode = string.Empty;

    [ObservableProperty]
    private string _twoFactorPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QrStatusTone))]
    private bool _isQrLoading = true;

    public Models.StatusTone QrStatusTone => HasError
        ? Models.StatusTone.Danger
        : (IsQrLoading ? Models.StatusTone.Neutral : Models.StatusTone.Success);

    [ObservableProperty]
    private string _qrStatusText = "Ожидание сканирования…";

    [ObservableProperty]
    private ImageSource? _qrCodeImage;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _hasProxyConfigured;

    public bool IsRecoveryMode => CurrentMode == AuthMode.SessionRecovery;
    public bool IsQrMode => CurrentMode == AuthMode.QrCode;
    public bool IsPhoneMode => CurrentMode is AuthMode.PhoneInput or AuthMode.CodeInput;
    public bool Is2FaMode => CurrentMode == AuthMode.TwoFactor;
    public bool IsSendCodeButtonVisible => CurrentMode == AuthMode.PhoneInput;
    public bool IsCodeInputVisible => CurrentMode == AuthMode.CodeInput;
    public bool IsPhoneInputEnabled => CurrentMode == AuthMode.PhoneInput;

    public AuthViewModel(
        ITelegramService telegramService,
        IDispatcherService dispatcher,
        IConfigService? configService = null)
        : base(dispatcher)
    {
        _telegramService = telegramService ?? throw new ArgumentNullException(nameof(telegramService));
        _configService = configService;
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HasError)) OnPropertyChanged(nameof(QrStatusTone));
        };
        RefreshProxyIndicator();
        WeakReferenceMessenger.Default.Register<AuthenticationStateChangedMessage>(this);
    }

    public void RefreshProxyIndicator()
    {
        if (_configService == null) return;
        HasProxyConfigured = !string.IsNullOrWhiteSpace(_configService.CurrentConfig.ProxyUrl);
    }

    /// <summary>
    /// Checks for existing active session on startup; if none, begins QR login flow.
    /// </summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (Interlocked.CompareExchange(ref _isInitializing, 1, 0) != 0)
        {
            return;
        }

        RefreshProxyIndicator();
        CurrentMode = AuthMode.SessionRecovery;
        StatusMessage = "Восстанавливаем подключение к Telegram…";
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (_telegramService.IsAuthorized)
            {
                OnAuthenticationSuccess();
                return;
            }

            bool hasSession = await _telegramService.CheckExistingSessionAsync();
            if (hasSession)
            {
                OnAuthenticationSuccess();
                return;
            }

            IsBusy = false;
            StartQrLogin();
        }
        catch
        {
            CurrentMode = AuthMode.SessionRecovery;
            StatusMessage = null;
            ErrorMessage = "Не удалось восстановить подключение. Проверьте сеть или настройки прокси и повторите попытку.";
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _isInitializing, 0);
        }
    }

    [RelayCommand]
    public void StartQrLogin()
    {
        if (IsBusy) return;
        CancelQrLogin();
        CurrentMode = AuthMode.QrCode;
        QrCodeImage = null;
        IsQrLoading = true;
        ErrorMessage = null;
        QrStatusText = "Генерация QR-кода…";
        StatusMessage = null;

        var source = new CancellationTokenSource();
        _qrCts = source;
        var ct = source.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                string result = await _telegramService.BeginLoginWithQrAsync(
                    qrUrl => HandleQrUrlGenerated(qrUrl, ct),
                    onPasswordNeeded: () =>
                    {
                        Dispatcher.Enqueue(() =>
                        {
                            if (ct.IsCancellationRequested) return;
                            CurrentMode = AuthMode.TwoFactor;
                            StatusMessage = "Требуется пароль двухфакторной аутентификации (2FA).";
                        });
                    },
                    ct);

                if (ct.IsCancellationRequested) return;

                if (result == "LOGIN_SUCCESS")
                {
                    Dispatcher.Enqueue(() => { if (!ct.IsCancellationRequested) OnAuthenticationSuccess(); });
                }
                else if (result == "password")
                {
                    Dispatcher.Enqueue(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        CurrentMode = AuthMode.TwoFactor;
                        StatusMessage = "Требуется пароль двухфакторной аутентификации (2FA).";
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when switching auth modes
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Dispatcher.Enqueue(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        IsQrLoading = false;
                        QrCodeImage = null;
                        ErrorMessage = MapTelegramError(ex.Message);
                        QrStatusText = "Ошибка генерации QR-кода. Попробуйте снова.";
                    });
                }
            }
            finally
            {
                Dispatcher.Enqueue(() =>
                {
                    if (ReferenceEquals(_qrCts, source)) _qrCts = null;
                    source.Dispose();
                });
            }
        });
    }

    private void HandleQrUrlGenerated(string qrUrl, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        var pixels = QrCodeRenderer.GenerateBgraPixels(qrUrl, out var width, out var height);
        Dispatcher.Enqueue(() =>
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var source = new WriteableBitmap(width, height);
                using var stream = source.PixelBuffer.AsStream();
                stream.Write(pixels);
                source.Invalidate();
                QrCodeImage = source;
                IsQrLoading = false;
                QrStatusText = "QR-код готов к сканированию";
            }
            catch
            {
                IsQrLoading = false;
                QrStatusText = "Обновите QR-код или войдите по телефону.";
                ErrorMessage = "Не удалось показать QR-код.";
            }
        });
    }

    [RelayCommand]
    public void SwitchToPhoneMode()
    {
        if (IsBusy) return;
        CancelQrLogin();
        CurrentMode = AuthMode.PhoneInput;
        PhoneCode = string.Empty;
        TwoFactorPassword = string.Empty;
        ErrorMessage = null;
        StatusMessage = null;
    }

    [RelayCommand]
    public void SwitchToQrMode()
    {
        StartQrLogin();
    }

    [RelayCommand]
    public async Task SendPhoneCodeAsync()
    {
        if (IsBusy || CurrentMode != AuthMode.PhoneInput) return;
        if (string.IsNullOrWhiteSpace(PhoneNumber) || PhoneNumber.Trim().Length < 6)
        {
            ErrorMessage = "Пожалуйста, введите корректный номер телефона с кодом страны (например, +79001234567).";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Отправка кода подтверждения…";

        try
        {
            string nextStep = await _telegramService.LoginWithPhoneAsync(PhoneNumber.Trim());
            if (nextStep == "verification_code")
            {
                CurrentMode = AuthMode.CodeInput;
                StatusMessage = "Код подтверждения отправлен в Telegram.";
            }
            else if (nextStep == "LOGIN_SUCCESS")
            {
                OnAuthenticationSuccess();
            }
            else if (nextStep == "password")
            {
                CurrentMode = AuthMode.TwoFactor;
                StatusMessage = "Требуется пароль двухфакторной аутентификации (2FA).";
            }
            else
            {
                ErrorMessage = "Этот способ подтверждения недоступен. Попробуйте вход по QR-коду.";
            }
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("PHONE_CODE_EXPIRED", StringComparison.OrdinalIgnoreCase)) CurrentMode = AuthMode.PhoneInput;
            ErrorMessage = MapTelegramError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SubmitPhoneCodeAsync()
    {
        if (IsBusy || CurrentMode != AuthMode.CodeInput) return;
        if (string.IsNullOrWhiteSpace(PhoneCode))
        {
            ErrorMessage = "Пожалуйста, введите код подтверждения.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Проверка кода…";

        try
        {
            string? result = await _telegramService.SubmitPhoneCodeAsync(PhoneCode.Trim());
            if (result == null || result == "LOGIN_SUCCESS")
            {
                OnAuthenticationSuccess();
            }
            else if (result == "password")
            {
                CurrentMode = AuthMode.TwoFactor;
                StatusMessage = "Требуется пароль двухфакторной аутентификации (2FA).";
            }
            else
            {
                ErrorMessage = "Неожиданный ответ сервера при проверке кода.";
            }
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("PHONE_CODE_EXPIRED", StringComparison.OrdinalIgnoreCase)) CurrentMode = AuthMode.PhoneInput;
            ErrorMessage = MapTelegramError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task Submit2FaAsync()
    {
        if (IsBusy || CurrentMode != AuthMode.TwoFactor) return;
        if (string.IsNullOrEmpty(TwoFactorPassword))
        {
            ErrorMessage = "Пожалуйста, введите облачный пароль 2FA.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Проверка пароля…";

        try
        {
            string? result = await _telegramService.SubmitPasswordAsync(TwoFactorPassword);
            if (result == null || result == "LOGIN_SUCCESS")
            {
                OnAuthenticationSuccess();
            }
            else
            {
                ErrorMessage = "Неверный пароль двухфакторной аутентификации.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = MapTelegramError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnAuthenticationSuccess()
    {
        if (IsAuthenticated) return;
        IsAuthenticated = true;
        PhoneCode = string.Empty;
        TwoFactorPassword = string.Empty;
        QrCodeImage = null;
        CancelQrLogin();
        ErrorMessage = null;
        StatusMessage = "Авторизация успешно завершена!";
        WeakReferenceMessenger.Default.Send(new AuthenticationStateChangedMessage(true));
    }

    public void Receive(AuthenticationStateChangedMessage message)
    {
        if (!message.Value)
        {
            Dispatcher.Enqueue(() =>
            {
                IsAuthenticated = false;
                PhoneNumber = string.Empty;
                PhoneCode = string.Empty;
                TwoFactorPassword = string.Empty;
                ErrorMessage = null;
                StatusMessage = null;
                RefreshProxyIndicator();
                StartQrLogin();
            });
        }
    }

    public void CancelQrLogin()
    {
        if (_qrCts != null)
        {
            _qrCts.Cancel();
            _qrCts = null;
        }
    }

    private static string MapTelegramError(string error)
    {
        if (error.Contains("PHONE_NUMBER_INVALID", StringComparison.OrdinalIgnoreCase))
            return "Неверный формат номера телефона.";
        if (error.Contains("PHONE_CODE_INVALID", StringComparison.OrdinalIgnoreCase))
            return "Неверный код подтверждения.";
        if (error.Contains("PHONE_CODE_EXPIRED", StringComparison.OrdinalIgnoreCase))
            return "Срок действия кода подтверждения истёк. Запросите новый.";
        if (error.Contains("PASSWORD_HASH_INVALID", StringComparison.OrdinalIgnoreCase))
            return "Неверный облачный пароль 2FA.";
        if (error.Contains("FLOOD_WAIT", StringComparison.OrdinalIgnoreCase))
            return "Слишком много попыток. Пожалуйста, подождите некоторое время.";
        if (error.Contains("session file", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("session block", StringComparison.OrdinalIgnoreCase))
            return "Файл сессии был сброшен для нового входа. Повторите сканирование QR-кода.";

        return "Не удалось войти. Проверьте подключение или настройки прокси и повторите попытку.";
    }

    public override void Dispose()
    {
        WeakReferenceMessenger.Default.Unregister<AuthenticationStateChangedMessage>(this);
        CancelQrLogin();
        base.Dispose();
    }
}
