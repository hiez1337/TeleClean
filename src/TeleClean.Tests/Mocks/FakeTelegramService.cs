using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;

namespace TeleClean.Tests.Mocks;

/// <summary>
/// Fake implementation of ITelegramService for offline, deterministic unit testing.
/// Mirrors Python FakeTelegramClient in tests/conftest.py with full C# MTProto contract support.
/// </summary>
public sealed class FakeTelegramService : ITelegramService
{
    private readonly Dictionary<long, LeaveResult> _configuredLeaveResults = new();
    private readonly List<DialogItem> _dialogs = new();
    private readonly List<(long DialogId, DialogType DialogType)> _leaveCalls = new();

    public bool IsConfigured { get; set; } = true;
    public bool IsAuthorized { get; set; } = true;

    public IReadOnlyList<(long DialogId, DialogType DialogType)> LeaveCalls => _leaveCalls;
    public IReadOnlyList<DialogItem> Dialogs => _dialogs;

    public Func<CancellationToken, Task<bool>>? CheckExistingSessionHandler { get; set; }
    public Func<Action<string>, CancellationToken, Task<string>>? BeginLoginWithQrHandler { get; set; }
    public Func<string, CancellationToken, Task<string>>? LoginWithPhoneHandler { get; set; }
    public Func<string, CancellationToken, Task<string>>? SubmitPhoneCodeHandler { get; set; }
    public Func<string, CancellationToken, Task<string>>? SubmitPasswordHandler { get; set; }
    public Func<long, CancellationToken, Task<string?>>? DownloadAvatarHandler { get; set; }

    public TimeSpan? RateLimitRetryAfter { get; set; }
    public Func<DialogItem, CancellationToken, Task<LeaveResult>>? LeaveHandler { get; set; }

    public Func<CancellationToken, Task<IReadOnlyList<DialogItem>>>? FetchDialogsHandler { get; set; }

    public bool ResetSessionCalled { get; private set; }

    public void SetDialogs(IEnumerable<DialogItem> dialogs)
    {
        _dialogs.Clear();
        _dialogs.AddRange(dialogs);
    }

    public void SetLeaveResult(long dialogId, LeaveResult result)
    {
        _configuredLeaveResults[dialogId] = result;
    }

    public void SetDefaultLeaveResult(LeaveResult result)
    {
        DefaultLeaveResult = result;
    }

    public LeaveResult DefaultLeaveResult { get; set; } = LeaveResult.Success;

    public Task<bool> CheckExistingSessionAsync(CancellationToken ct = default)
    {
        if (CheckExistingSessionHandler != null)
        {
            return CheckExistingSessionHandler(ct);
        }
        return Task.FromResult(IsAuthorized);
    }

    public Task<string> BeginLoginWithQrAsync(Action<string> onQrUrlGenerated, Action? onPasswordNeeded = null, CancellationToken ct = default)
    {
        if (BeginLoginWithQrHandler != null)
        {
            return BeginLoginWithQrHandler(onQrUrlGenerated, ct);
        }

        if (IsAuthorized)
        {
            return Task.FromResult("LOGIN_SUCCESS");
        }

        onQrUrlGenerated("tg://login?token=fake_test_qr_token_offline");
        return Task.FromResult("waiting");
    }

    public Task<string> LoginWithPhoneAsync(string phone, CancellationToken ct = default)
    {
        if (LoginWithPhoneHandler != null)
        {
            return LoginWithPhoneHandler(phone, ct);
        }
        return Task.FromResult(IsAuthorized ? "LOGIN_SUCCESS" : "verification_code");
    }

    public Task<string> SubmitPhoneCodeAsync(string code, CancellationToken ct = default)
    {
        if (SubmitPhoneCodeHandler != null)
        {
            return SubmitPhoneCodeHandler(code, ct);
        }
        return Task.FromResult(IsAuthorized ? "LOGIN_SUCCESS" : "verification_code");
    }

    public Task<string> SubmitPasswordAsync(string password, CancellationToken ct = default)
    {
        if (SubmitPasswordHandler != null)
        {
            return SubmitPasswordHandler(password, ct);
        }
        return Task.FromResult(IsAuthorized ? "LOGIN_SUCCESS" : "password");
    }

    public Task ResetSessionAsync(CancellationToken ct = default)
    {
        ResetSessionCalled = true;
        IsAuthorized = false;
        _dialogs.Clear();
        return Task.CompletedTask;
    }

    public Task RecreateClientAsync()
    {
        // No-op in fake: no real client to dispose
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DialogItem>> FetchDialogsAsync(CancellationToken ct = default)
    {
        if (FetchDialogsHandler != null) return FetchDialogsHandler(ct);
        return Task.FromResult<IReadOnlyList<DialogItem>>(_dialogs.Select(d => d.Clone()).ToList());
    }

    public Task<LeaveResult> LeaveDialogAsync(DialogItem dialog, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        _leaveCalls.Add((dialog.Id, dialog.DialogType));
        if (LeaveHandler != null) return LeaveHandler(dialog, ct);

        if (_configuredLeaveResults.TryGetValue(dialog.Id, out var result))
        {
            if (result == LeaveResult.Success)
            {
                dialog.IsJoined = false;
            }
            return Task.FromResult(result);
        }

        if (DefaultLeaveResult == LeaveResult.Success)
        {
            dialog.IsJoined = false;
        }
        return Task.FromResult(DefaultLeaveResult);
    }

    public Task<string?> DownloadAvatarAsync(DialogItem dialog, CancellationToken ct = default)
    {
        if (DownloadAvatarHandler != null)
        {
            return DownloadAvatarHandler(dialog.Id, ct);
        }
        return Task.FromResult<string?>($"/fake/avatars/{dialog.Id}.jpg");
    }
}
