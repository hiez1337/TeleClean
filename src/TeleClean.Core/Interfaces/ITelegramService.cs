using TeleClean.Core.Models;

namespace TeleClean.Core.Interfaces;

/// <summary>
/// Service abstraction for MTProto Telegram client interactions.
/// </summary>
public interface ITelegramService
{
    /// <summary>
    /// Indicates whether Telegram API credentials (ID and Hash) are configured.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Indicates whether an authorized user session is currently active.
    /// </summary>
    bool IsAuthorized { get; }

    /// <summary>
    /// Checks whether an existing session on disk can be resumed without re-authenticating.
    /// </summary>
    Task<bool> CheckExistingSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts QR code authentication flow, emitting tg://login URLs via callback.
    /// Returns "LOGIN_SUCCESS" on success, or raises exception / 2FA indicator.
    /// </summary>
    Task<string> BeginLoginWithQrAsync(Action<string> onQrUrlGenerated, Action? onPasswordNeeded = null, CancellationToken ct = default);

    /// <summary>
    /// Sends verification code request to the provided phone number.
    /// Returns the next required step (e.g. "verification_code").
    /// </summary>
    Task<string> LoginWithPhoneAsync(string phone, CancellationToken ct = default);

    /// <summary>
    /// Submits received verification code.
    /// Returns null if authentication succeeded, or "password" if 2FA is needed.
    /// </summary>
    Task<string> SubmitPhoneCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Submits 2FA cloud password.
    /// Returns null if authentication succeeded.
    /// </summary>
    Task<string> SubmitPasswordAsync(string password, CancellationToken ct = default);

    /// <summary>
    /// Remotely invalidates session on Telegram servers and purges local session files.
    /// </summary>
    Task ResetSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Disposes the current WTelegramClient instance WITHOUT server logout or session deletion,
    /// so the next auth call recreates it (e.g., to pick up new proxy settings).
    /// </summary>
    Task RecreateClientAsync();

    /// <summary>
    /// Retrieves all user dialogs and maps them into the 6 TeleClean dialog types.
    /// </summary>
    Task<IReadOnlyList<DialogItem>> FetchDialogsAsync(CancellationToken ct = default);

    /// <summary>
    /// Dispatches leave or delete request based on dialog type.
    /// </summary>
    Task<LeaveResult> LeaveDialogAsync(DialogItem dialog, CancellationToken ct = default);

    /// <summary>Server-requested wait from the last rate-limited action, when available.</summary>
    TimeSpan? RateLimitRetryAfter => null;

    /// <summary>
    /// Downloads dialog avatar / profile photo and returns local cached file path.
    /// </summary>
    Task<string?> DownloadAvatarAsync(DialogItem dialog, CancellationToken ct = default);
}
