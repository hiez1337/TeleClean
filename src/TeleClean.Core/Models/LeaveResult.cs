using System.Text.Json.Serialization;

namespace TeleClean.Core.Models;

/// <summary>
/// Outcome of a single dialog leave or delete operation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LeaveResult
{
    Success,
    RateLimited,
    Forbidden,
    NotFound,
    Error
}
