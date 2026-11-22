using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;

namespace TeleClean.App.Services;

// The demo has no Telegram client and never reads credentials or a real session.
internal sealed class DemoTelegramService(bool authorized) : ITelegramService
{
    private readonly List<DialogItem> _dialogs = CreateDialogs();

    public bool IsConfigured => true;
    public bool IsAuthorized { get; private set; } = authorized;

    public Task<bool> CheckExistingSessionAsync(CancellationToken ct = default) =>
        Task.FromResult(IsAuthorized);

    public async Task<string> BeginLoginWithQrAsync(Action<string> onQrUrlGenerated,
        Action? onPasswordNeeded = null, CancellationToken ct = default)
    {
        onQrUrlGenerated("TeleClean demo: offline example");
        await Task.Delay(Timeout.Infinite, ct);
        return string.Empty;
    }

    public Task<string> LoginWithPhoneAsync(string phone, CancellationToken ct = default) =>
        Task.FromResult("verification_code");

    public Task<string> SubmitPhoneCodeAsync(string code, CancellationToken ct = default)
    {
        IsAuthorized = true;
        return Task.FromResult("LOGIN_SUCCESS");
    }

    public Task<string> SubmitPasswordAsync(string password, CancellationToken ct = default) =>
        SubmitPhoneCodeAsync(password, ct);

    public Task ResetSessionAsync(CancellationToken ct = default)
    {
        IsAuthorized = false;
        return Task.CompletedTask;
    }

    public Task RecreateClientAsync() => Task.CompletedTask;

    public async Task<IReadOnlyList<DialogItem>> FetchDialogsAsync(CancellationToken ct = default)
    {
        await Task.Delay(250, ct);
        return _dialogs.Select(dialog => dialog.Clone()).ToArray();
    }

    public async Task<LeaveResult> LeaveDialogAsync(DialogItem dialog, CancellationToken ct = default)
    {
        await Task.Delay(350, ct);
        _dialogs.RemoveAll(item => item.Id == dialog.Id && item.DialogType == dialog.DialogType);
        return LeaveResult.Success;
    }

    public Task<string?> DownloadAvatarAsync(DialogItem dialog, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    private static List<DialogItem> CreateDialogs()
    {
        (string Title, string? Username, DialogType Type, int Participants, int Unread)[] samples =
        [
            ("Архитектура города", "demo_architecture", DialogType.Channel, 34, 0),
            ("Бюро переводов", "demo_translate", DialogType.Channel, 2317, 5),
            ("Дизайн-среда", "demo_design", DialogType.Channel, 2234, 0),
            ("Заметки о кино", "demo_cinema", DialogType.Channel, 37, 0),
            ("Книжный обмен", "demo_books", DialogType.Channel, 2068, 8),
            ("Лаборатория звука", "demo_sound", DialogType.Channel, 1985, 0),
            ("Маршруты выходного дня", "demo_routes", DialogType.Channel, 40, 0),
            ("Научный обзор", "demo_science", DialogType.Channel, 1819, 11),
            ("Площадка для волонтёров", null, DialogType.Supergroup, 91, 3),
            ("Клуб настольных игр", null, DialogType.Group, 28, 0),
            ("Бот напоминаний", "demo_reminders", DialogType.Bot, 0, 0),
            ("Удалённый аккаунт", null, DialogType.Deleted, 0, 0),
        ];
        return samples.Select((sample, index) => new DialogItem
        {
            Id = 3_000_000_000L + index,
            Title = sample.Title,
            Username = sample.Username,
            DialogType = sample.Type,
            ParticipantCount = sample.Participants,
            UnreadCount = sample.Unread,
            IsActive = sample.Type != DialogType.Deleted,
            LastActivity = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc).AddDays(-index)
        }).ToList();
    }
}
