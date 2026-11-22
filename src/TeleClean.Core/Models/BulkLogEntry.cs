using System.Text.Json.Serialization;

namespace TeleClean.Core.Models;

/// <summary>
/// Represents a single logged event in the bulk leave process or historical audit log.
/// </summary>
public sealed class BulkLogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("dialog_id")]
    public long DialogId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public LeaveResult Result { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = "Выход";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonIgnore]
    public string FormattedTime => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    [JsonIgnore]
    public string StatusText => Result switch
    {
        LeaveResult.Success => "Успешно",
        LeaveResult.RateLimited => "FloodWait",
        LeaveResult.Forbidden => "Запрещено",
        LeaveResult.Error => "Ошибка",
        _ => "Неизвестно"
    };

    public BulkLogEntry()
    {
    }

    public BulkLogEntry(DateTime timestamp, long dialogId, string title, LeaveResult result, string message, string action = "Выход")
    {
        Timestamp = timestamp;
        DialogId = dialogId;
        Title = title;
        Result = result;
        Message = message;
        Action = action;
    }
}
