using System.Text.Json.Serialization;

namespace TeleClean.Core.Models;

/// <summary>
/// Progress snapshot emitted during bulk operations.
/// </summary>
public sealed class BulkProgressReport
{
    [JsonPropertyName("current_index")]
    public int CurrentIndex { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("current_title")]
    public string CurrentTitle { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public BulkOperationState State { get; set; } = BulkOperationState.Idle;

    [JsonPropertyName("seconds_remaining")]
    public int SecondsRemaining { get; set; }

    [JsonPropertyName("success_count")]
    public int SuccessCount { get; set; }

    [JsonPropertyName("error_count")]
    public int ErrorCount { get; set; }

    public double Percent => TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100.0 : 0.0;

    public BulkProgressReport()
    {
    }

    public BulkProgressReport(
        int currentIndex,
        int totalCount,
        string currentTitle,
        BulkOperationState state,
        int secondsRemaining,
        int successCount = 0,
        int errorCount = 0)
    {
        CurrentIndex = currentIndex;
        TotalCount = totalCount;
        CurrentTitle = currentTitle;
        State = state;
        SecondsRemaining = secondsRemaining;
        SuccessCount = successCount;
        ErrorCount = errorCount;
    }
}
