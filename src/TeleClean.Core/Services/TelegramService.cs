using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;
using TL;
using WTelegram;

namespace TeleClean.Core.Services;

/// <summary>
/// Production implementation of ITelegramService wrapping WTelegramClient.
/// </summary>
public sealed class TelegramService : ITelegramService, IAsyncDisposable
{
    private readonly IConfigService _configService;
    private readonly ICacheService _cacheService;
    private readonly SemaphoreSlim _clientLock = new(1, 1);

    private enum LoginFlow
    {
        None,
        Qr,
        Phone
    }

    private Client? _client;
    private string? _sessionPath;
    private volatile bool _disposed;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private Messages_Dialogs? _cachedDialogs;
    private LoginFlow _activeLoginFlow = LoginFlow.None;

    // 2FA coordination during QR login flow
    private TaskCompletionSource<string>? _passwordInputTcs;
    private TaskCompletionSource<string?>? _passwordResultTcs;
    private Action? _onPasswordNeeded;
    private CancellationToken _qrLoginToken;

    public bool IsConfigured
    {
        get
        {
            var (apiId, apiHash) = _configService.GetEffectiveCredentials();
            return apiId > 0 && !string.IsNullOrWhiteSpace(apiHash);
        }
    }

    public TimeSpan? RateLimitRetryAfter { get; private set; }

    public bool IsAuthorized => _client?.User != null && _client.UserId != 0;

    public TelegramService(IConfigService configService, ICacheService cacheService)
    {
        _configService = configService;
        _cacheService = cacheService;
    }

    public string GetSessionPath()
    {
        if (_sessionPath != null) return _sessionPath;
        var dataDirectory = _configService.GetDataDirectory();
        var configured = _configService.CurrentConfig.SessionPath;
        return _sessionPath = !string.IsNullOrWhiteSpace(configured) &&
            !Path.GetFileName(configured).Equals("teleclean.session", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(configured, dataDirectory)
            : Path.Combine(dataDirectory, "teleclean_v2.session");
    }

    private void EnsureClientCreated()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_client != null) return;

        var (apiId, apiHash) = _configService.GetEffectiveCredentials();
        var sessionPath = GetSessionPath();

        var sessionDir = Path.GetDirectoryName(sessionPath);
        if (!string.IsNullOrEmpty(sessionDir) && !Directory.Exists(sessionDir))
        {
            Directory.CreateDirectory(sessionDir);
        }

        SanitizeSessionFile(sessionPath);

        _client = new Client(ConfigCallback);

        // WTelegramClient auto retries flood waits up to 60s
        _client.FloodRetryThreshold = 60;

        // Apply proxy if configured
        var proxyUrl = _configService.CurrentConfig.ProxyUrl;
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            _client.MTProxyUrl = NormalizeProxyUrl(proxyUrl.Trim());
        }
    }

    private string? ConfigCallback(string what)
    {
        var (apiId, apiHash) = _configService.GetEffectiveCredentials();
        var sessionPath = GetSessionPath();

        switch (what)
        {
            case "api_id":
                return apiId.ToString();
            case "api_hash":
                return apiHash;
            // QR sessions have no phone_number. Trust the user stored in this local session.
            case "user_id":
                return "-1";
            case "session_pathname":
                return sessionPath;
            case "password":
                return HandlePasswordConfig();
            default:
                return null;
        }
    }

    private string? HandlePasswordConfig()
    {
        // If a previous attempt was waiting for result, signal that it was rejected
        var prevResultTcs = Interlocked.Exchange(ref _passwordResultTcs, null);
        prevResultTcs?.TrySetResult("password");

        // Wait for user to supply the password via SubmitPasswordAsync
        var inputTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _passwordInputTcs = inputTcs;
        using var cancellation = _qrLoginToken.Register(() => inputTcs.TrySetCanceled(_qrLoginToken));
        _onPasswordNeeded?.Invoke();

        try
        {
            return inputTcs.Task.GetAwaiter().GetResult();
        }
        finally
        {
            Interlocked.CompareExchange(ref _passwordInputTcs, null, inputTcs);
        }
    }

    private static void SanitizeSessionFile(string sessionPath)
    {
        try
        {
            if (!File.Exists(sessionPath)) return;

            var fileInfo = new FileInfo(sessionPath);
            if (fileInfo.Length == 0)
            {
                File.Delete(sessionPath);
                return;
            }

            // Preserve incompatible SQLite files for recovery.
            using var fs = new FileStream(sessionPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length >= 16)
            {
                byte[] header = new byte[16];
                int read = fs.Read(header, 0, 16);
                if (read >= 6 && header[0] == 'S' && header[1] == 'Q' && header[2] == 'L' && header[3] == 'i' && header[4] == 't' && header[5] == 'e')
                {
                    fs.Dispose();
                    File.Move(sessionPath, sessionPath + ".backup-" + Guid.NewGuid().ToString("N"));
                }
            }
        }
        catch
        {
            // Ignore sanitization read errors
        }
    }

    /// <summary>
    /// Converts tg://proxy? deep-link scheme to the https://t.me/proxy? format
    /// that WTelegramClient's MTProxyUrl property requires.
    /// </summary>
    private static string NormalizeProxyUrl(string url)
    {
        const string tgScheme = "tg://proxy?";
        if (url.StartsWith(tgScheme, StringComparison.OrdinalIgnoreCase))
            return "https://t.me/proxy?" + url[tgScheme.Length..];
        return url;
    }

    private async Task HandleCorruptedSessionAsync(Exception ex)
    {
        if (ex.Message.Contains("session file", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("session block", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (_client != null)
                {
                    await _client.DisposeAsync();
                    _client = null;
                }

                var sessionPath = GetSessionPath();
                if (File.Exists(sessionPath))
                {
                    File.Move(sessionPath, sessionPath + ".backup-" + Guid.NewGuid().ToString("N"));
                }

                EnsureClientCreated();
            }
            catch
            {
                // Best effort
            }
        }
    }

    public async Task<bool> CheckExistingSessionAsync(CancellationToken ct = default)
    {
        await _clientLock.WaitAsync(ct);
        try
        {
            if (IsAuthorized)
            {
                return true;
            }

            var sessionPath = GetSessionPath();
            if (!File.Exists(sessionPath) || new FileInfo(sessionPath).Length == 0)
            {
                return false;
            }

            EnsureClientCreated();
            if (_client == null)
            {
                return false;
            }

            var user = await _client.LoginUserIfNeeded(reloginOnFailedResume: false);
            if (user != null && _client.UserId != 0)
            {

                return true;
            }
            return false;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public async Task<string> BeginLoginWithQrAsync(
        Action<string> onQrUrlGenerated,
        Action? onPasswordNeeded = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(onQrUrlGenerated);

        await _clientLock.WaitAsync(ct);
        try
        {
            _qrLoginToken = ct;
            _activeLoginFlow = LoginFlow.Qr;
            _onPasswordNeeded = onPasswordNeeded;
            _passwordInputTcs = null;
            _passwordResultTcs = null;
            EnsureClientCreated();

            if (IsAuthorized)
            {
                _activeLoginFlow = LoginFlow.None;

                return "LOGIN_SUCCESS";
            }

            try
            {
                var user = await _client!.LoginWithQRCode(onQrUrlGenerated, ct: ct);
                if (user != null)
                {
                    _activeLoginFlow = LoginFlow.None;
                    _passwordResultTcs?.TrySetResult("LOGIN_SUCCESS");
                    _passwordInputTcs = null;
                    _passwordResultTcs = null;

                    return "LOGIN_SUCCESS";
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _passwordInputTcs?.TrySetCanceled(ct);
                _passwordResultTcs?.TrySetCanceled(ct);
                throw;
            }
            catch (Exception ex) when (ex.Message.Contains("session file", StringComparison.OrdinalIgnoreCase) ||
                                       ex.Message.Contains("session block", StringComparison.OrdinalIgnoreCase))
            {
                await HandleCorruptedSessionAsync(ex);
                var user = await _client!.LoginWithQRCode(onQrUrlGenerated, ct: ct);
                if (user != null)
                {
                    _activeLoginFlow = LoginFlow.None;
                    _passwordResultTcs?.TrySetResult("LOGIN_SUCCESS");
                    _passwordInputTcs = null;
                    _passwordResultTcs = null;

                    return "LOGIN_SUCCESS";
                }
            }
            catch (Exception ex)
            {
                _passwordResultTcs?.TrySetException(ex);
                _passwordInputTcs?.TrySetException(ex);
                throw;
            }

            if (IsAuthorized)
            {
                _activeLoginFlow = LoginFlow.None;
                _passwordResultTcs?.TrySetResult("LOGIN_SUCCESS");
                _passwordInputTcs = null;
                _passwordResultTcs = null;

                return "LOGIN_SUCCESS";
            }

            return "waiting";
        }
        finally
        {
            _activeLoginFlow = LoginFlow.None;
            _onPasswordNeeded = null;
            if (ct.IsCancellationRequested) _passwordResultTcs?.TrySetCanceled(ct);
            _clientLock.Release();
        }
    }

    public async Task<string> LoginWithPhoneAsync(string phone, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phone);

        await _clientLock.WaitAsync(ct);
        try
        {
            _activeLoginFlow = LoginFlow.Phone;
            EnsureClientCreated();
            var needed = await _client!.Login(phone);
            if (needed == null && IsAuthorized)
            {
                _activeLoginFlow = LoginFlow.None;

                return "LOGIN_SUCCESS";
            }
            return needed ?? (IsAuthorized ? "LOGIN_SUCCESS" : "verification_code");
        }
        catch (Exception ex) when (ex.Message.Contains("session file", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("session block", StringComparison.OrdinalIgnoreCase))
        {
            await HandleCorruptedSessionAsync(ex);
            var needed = await _client!.Login(phone);
            if (needed == null && IsAuthorized)
            {
                _activeLoginFlow = LoginFlow.None;
                return "LOGIN_SUCCESS";
            }
            return needed ?? (IsAuthorized ? "LOGIN_SUCCESS" : "verification_code");
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public async Task<string> SubmitPhoneCodeAsync(string code, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        await _clientLock.WaitAsync(ct);
        try
        {
            EnsureClientCreated();
            var needed = await _client!.Login(code);
            if (needed == null && IsAuthorized)
            {
                _activeLoginFlow = LoginFlow.None;
                return "LOGIN_SUCCESS";
            }
            return needed ?? (IsAuthorized ? "LOGIN_SUCCESS" : "verification_code");
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public async Task<string> SubmitPasswordAsync(string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        // Path 1: Check if an active 2FA password request (e.g. from QR flow) is waiting for input
        var inputTcs = _passwordInputTcs;
        if (inputTcs != null && !inputTcs.Task.IsCompleted)
        {
            var resultTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _passwordResultTcs = resultTcs;

            using (ct.Register(() =>
            {
                resultTcs.TrySetCanceled(ct);
                inputTcs.TrySetCanceled(ct);
            }))
            {
                if (inputTcs.TrySetResult(password))
                {
                    try
                    {
                        var res = await resultTcs.Task;
                        return res ?? "LOGIN_SUCCESS";
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (RpcException ex) when (ex.Code == 400 && ex.Message.Contains("PASSWORD_HASH_INVALID", StringComparison.OrdinalIgnoreCase))
                    {
                        return "password";
                    }
                }
            }
        }

        // Path 2: Phone login flow or standalone MTProto 2FA completion
        await _clientLock.WaitAsync(ct);
        try
        {
            EnsureClientCreated();

            if (_activeLoginFlow == LoginFlow.Phone)
            {
                try
                {
                    var needed = await _client!.Login(password);
                    if (needed == null && IsAuthorized)
                    {
                        _activeLoginFlow = LoginFlow.None;

                        return "LOGIN_SUCCESS";
                    }
                    return needed ?? (IsAuthorized ? "LOGIN_SUCCESS" : "password");
                }
                catch (RpcException ex) when (ex.Code == 400 && ex.Message.Contains("PASSWORD_HASH_INVALID", StringComparison.OrdinalIgnoreCase))
                {
                    return "password";
                }
            }

            // Direct MTProto 2FA completion (handles QR-initiated login and standalone 2FA)
            try
            {
                var accountPassword = await _client!.Account_GetPassword();
                var srp = await Client.InputCheckPassword(accountPassword, password);
                var auth = await _client!.Auth_CheckPassword(srp);
                var user = _client!.LoginAlreadyDone(auth);
                if (user != null && _client.UserId != 0)
                {
                    _activeLoginFlow = LoginFlow.None;

                    return "LOGIN_SUCCESS";
                }
                return "password";
            }
            catch (RpcException ex) when (ex.Code == 400 && ex.Message.Contains("PASSWORD_HASH_INVALID", StringComparison.OrdinalIgnoreCase))
            {
                return "password";
            }
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public async Task ResetSessionAsync(CancellationToken ct = default)
    {
        await _clientLock.WaitAsync(ct);
        try
        {
            _activeLoginFlow = LoginFlow.None;
            _onPasswordNeeded = null;
            _passwordInputTcs?.TrySetCanceled();
            _passwordInputTcs = null;
            _passwordResultTcs?.TrySetCanceled();
            _passwordResultTcs = null;

            var sessionPath = GetSessionPath();
            if (_client != null)
            {
                if (IsAuthorized) await _client.Auth_LogOut();
                await _client.DisposeAsync();
                _client = null;
            }
            _cachedDialogs = null;
            File.Delete(sessionPath);

            // Clear cached dialogs
            await _cacheService.ClearCacheAsync(ct);
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public async Task RecreateClientAsync()
    {
        await _clientLock.WaitAsync();
        try
        {
            _activeLoginFlow = LoginFlow.None;
            _onPasswordNeeded = null;
            _passwordInputTcs?.TrySetCanceled();
            _passwordInputTcs = null;
            _passwordResultTcs?.TrySetCanceled();
            _passwordResultTcs = null;

            if (_client != null)
            {
                try { await _client.DisposeAsync(); } catch { /* ignore */ }
                _client = null;
                _cachedDialogs = null;
            }
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public async Task<IReadOnlyList<DialogItem>> FetchDialogsAsync(CancellationToken ct = default)
    {
        await _clientLock.WaitAsync(ct);
        try
        {
            EnsureClientCreated();

            if (!IsAuthorized)
            {
                var ok = await _client!.LoginUserIfNeeded();
                if (ok == null)
                {
                    throw new InvalidOperationException("Telegram client is not authorized.");
                }
            }

            var dialogs = await _client!.Messages_GetAllDialogs();
            _cachedDialogs = dialogs;

            var result = new List<DialogItem>(dialogs.dialogs.Length);

            Dictionary<(Type, long, int), TL.MessageBase>? messageDict = null;
            if (dialogs.messages != null)
            {
                messageDict = new Dictionary<(Type, long, int), TL.MessageBase>(dialogs.messages.Length);
                foreach (var msg in dialogs.messages)
                {
                    if (msg?.Peer is { } peer)
                    {
                        messageDict[(peer.GetType(), peer.ID, msg.ID)] = msg;
                    }
                }
            }

            foreach (var d in dialogs.dialogs)
            {
                ct.ThrowIfCancellationRequested();

                var peerInfo = dialogs.UserOrChat(d.Peer);
                if (peerInfo == null) continue;

                int unreadCount = d is TL.Dialog dialog ? dialog.unread_count : 0;
                DateTime? lastActivity = null;
                if (d is TL.Dialog dl && dl.top_message > 0 && messageDict != null && messageDict.TryGetValue((d.Peer.GetType(), d.Peer.ID, dl.top_message), out var topMsg))
                {
                    try { lastActivity = topMsg.Date; } catch { /* ignore date parse */ }
                }

                DialogItem? item = peerInfo switch
                {
                    // Channel or Supergroup
                    TL.Channel channel => new DialogItem
                    {
                        Id = channel.id,
                        AccessHash = channel.access_hash,
                        Title = channel.title ?? "Канал",
                        Username = channel.MainUsername,
                        ParticipantCount = channel.participants_count,
                        UnreadCount = unreadCount,
                        IsChannel = channel.IsChannel,
                        IsJoined = channel.IsActive,
                        IsActive = channel.IsActive,
                        LastActivity = lastActivity,
                        DialogType = channel.IsChannel ? DialogType.Channel : DialogType.Supergroup
                    },

                    // Basic legacy Group (Chat)
                    TL.Chat chat when !chat.flags.HasFlag(TL.Chat.Flags.deactivated) => new DialogItem
                    {
                        Id = chat.id,
                        AccessHash = 0,
                        Title = chat.title ?? "Группа",
                        Username = null,
                        ParticipantCount = chat.participants_count,
                        UnreadCount = unreadCount,
                        IsChannel = false,
                        IsJoined = chat.IsActive,
                        IsActive = chat.IsActive,
                        LastActivity = lastActivity,
                        DialogType = DialogType.Group
                    },

                    // Deleted Account
                    TL.User user when !user.IsActive => new DialogItem
                    {
                        Id = user.id,
                        AccessHash = user.access_hash,
                        Title = user.ToString() ?? "Удалённый аккаунт",
                        Username = null,
                        ParticipantCount = 0,
                        UnreadCount = unreadCount,
                        IsChannel = false,
                        IsJoined = true,
                        IsActive = false,
                        LastActivity = lastActivity,
                        DialogType = DialogType.Deleted
                    },

                    // Bot
                    TL.User bot when bot.IsBot => new DialogItem
                    {
                        Id = bot.id,
                        AccessHash = bot.access_hash,
                        Title = bot.ToString() ?? "Бот",
                        Username = bot.MainUsername,
                        ParticipantCount = 0,
                        UnreadCount = unreadCount,
                        IsChannel = false,
                        IsJoined = true,
                        IsActive = true,
                        LastActivity = lastActivity,
                        DialogType = DialogType.Bot
                    },

                    // Regular active User
                    TL.User user => new DialogItem
                    {
                        Id = user.id,
                        AccessHash = user.access_hash,
                        Title = user.ToString() ?? "Пользователь",
                        Username = user.MainUsername,
                        ParticipantCount = 0,
                        UnreadCount = unreadCount,
                        IsChannel = false,
                        IsJoined = true,
                        IsActive = user.IsActive,
                        LastActivity = lastActivity,
                        DialogType = DialogType.User
                    },

                    _ => null
                };

                if (item != null)
                {
                    result.Add(item);
                }
            }

            return result;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public async Task<LeaveResult> LeaveDialogAsync(DialogItem dialog, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        await _clientLock.WaitAsync(ct);
        try
        {
            EnsureClientCreated();

            if (!IsAuthorized)
            {
                return LeaveResult.Error;
            }

            switch (dialog.DialogType)
            {
                case DialogType.Channel:
                case DialogType.Supergroup:
                    var inputChannel = new TL.InputChannel(dialog.Id, dialog.AccessHash);
                    await _client!.Channels_LeaveChannel(inputChannel);
                    dialog.IsJoined = false;
                    return LeaveResult.Success;

                case DialogType.Group:
                    await _client!.Messages_DeleteChatUser(dialog.Id, TL.InputUser.Self);
                    dialog.IsJoined = false;
                    return LeaveResult.Success;

                case DialogType.Bot:
                case DialogType.User:
                case DialogType.Deleted:
                    var inputPeer = new TL.InputPeerUser(dialog.Id, dialog.AccessHash);
                    await _client!.Messages_DeleteHistory(inputPeer, max_id: 0, just_clear: false, revoke: false);
                    dialog.IsJoined = false;
                    return LeaveResult.Success;

                default:
                    return LeaveResult.Error;
            }
        }
        catch (RpcException ex) when (ex.Code == 420) // FLOOD_WAIT_X
        {
            RateLimitRetryAfter = ex.X > 0 ? TimeSpan.FromSeconds(ex.X) : null;
            return LeaveResult.RateLimited;
        }
        catch (RpcException ex) when (ex.Code == 400 && ex.Message.Contains("CHANNEL_PRIVATE", StringComparison.OrdinalIgnoreCase))
        {
            return LeaveResult.Forbidden;
        }
        catch (RpcException ex) when (ex.Code == 400 && ex.Message.Contains("CHANNEL_INVALID", StringComparison.OrdinalIgnoreCase))
        {
            return LeaveResult.NotFound;
        }
        catch (RpcException ex) when (ex.Code == 403)
        {
            return LeaveResult.Forbidden;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LeaveResult.Error;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public async Task<string?> DownloadAvatarAsync(DialogItem item, CancellationToken ct = default)
    {
        var dialogId = item.AvatarCacheKey;
        if (_cacheService.HasAvatar(dialogId))
        {
            return _cacheService.GetAvatarPath(dialogId);
        }

        await _clientLock.WaitAsync(ct);
        try
        {
            EnsureClientCreated();

            if (_cachedDialogs == null)
            {
                return null;
            }

            var dialog = Array.Find(_cachedDialogs.dialogs, d => d.Peer.ID == item.Id && (d.Peer switch
            {
                PeerChannel => item.DialogType is DialogType.Channel or DialogType.Supergroup,
                PeerChat => item.DialogType == DialogType.Group,
                PeerUser => item.DialogType is DialogType.User or DialogType.Bot or DialogType.Deleted,
                _ => false
            }));
            if (dialog == null) return null;

            var peerInfo = _cachedDialogs.UserOrChat(dialog.Peer);
            if (peerInfo == null) return null;

            using var ms = new MemoryStream();
            var fileType = await _client!.DownloadProfilePhotoAsync(peerInfo, ms, big: false);
            if (fileType != Storage_FileType.unknown && ms.Length > 0)
            {
                ms.Position = 0;
                return await _cacheService.SaveAvatarAsync(dialogId, ms, ct);
            }

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        _onPasswordNeeded = null;
        _passwordInputTcs?.TrySetCanceled();
        _passwordResultTcs?.TrySetCanceled();

        // Interrupt pending RPC/reconnect waits before acquiring the operation lock.
        var closingClient = _client;
        if (closingClient != null) await closingClient.DisposeAsync();

        await _clientLock.WaitAsync();
        try
        {
            // A constructor that was already running may have completed during shutdown.
            if (_client != null && !ReferenceEquals(_client, closingClient))
                await _client.DisposeAsync();
            _client = null;
            _cachedDialogs = null;
        }
        finally
        {
            _clientLock.Release();
        }
    }
}
