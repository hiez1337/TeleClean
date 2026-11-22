using System.Text.Json.Serialization;

namespace TeleClean.Core.Models;

/// <summary>
/// Execution state of the bulk leave engine.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BulkOperationState
{
    Idle,
    Running,
    Paused,
    RateLimited,
    Completed,
    Interrupted
}
