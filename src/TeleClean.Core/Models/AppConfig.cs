using System.Text.Json.Serialization;

namespace TeleClean.Core.Models;

/// <summary>
/// Nested credentials object matching legacy Python config.json schema.
/// </summary>
public sealed class ApiKeysConfig
{
    [JsonPropertyName("api_id")]
    public int? ApiId { get; set; }

    [JsonPropertyName("api_hash")]
    public string? ApiHash { get; set; }
}

/// <summary>
/// Application settings and configuration.
/// </summary>
public sealed class AppConfig
{
    public const int DefaultBuildApiId = 11600115;
    public const string DefaultBuildApiHash = "dabf2aadd76c3a56983809d54c0760c6";

    [JsonPropertyName("api_id")]
    public int ApiId { get; set; } = DefaultBuildApiId;

    [JsonPropertyName("api_hash")]
    public string ApiHash { get; set; } = DefaultBuildApiHash;

    [JsonPropertyName("api_keys")]
    public ApiKeysConfig? ApiKeys { get; set; }

    [JsonPropertyName("session_path")]
    public string? SessionPath { get; set; }

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark";

    [JsonPropertyName("backdrop")]
    public string Backdrop { get; set; } = "Mica";

    [JsonPropertyName("default_delay_seconds")]
    public double DefaultDelaySeconds { get; set; } = 3.0;

    [JsonPropertyName("avatar_cache_dir")]
    public string? AvatarCacheDir { get; set; }

    [JsonPropertyName("proxy_url")]
    public string? ProxyUrl { get; set; }

    /// <summary>
    /// Resolves effective ApiId taking nested api_keys into account if present.
    /// </summary>
    public int GetEffectiveApiId() => (ApiKeys?.ApiId > 0) ? ApiKeys.ApiId.Value : (ApiId > 0 ? ApiId : DefaultBuildApiId);

    /// <summary>
    /// Resolves effective ApiHash taking nested api_keys into account if present.
    /// </summary>
    public string GetEffectiveApiHash() => !string.IsNullOrWhiteSpace(ApiKeys?.ApiHash) ? ApiKeys.ApiHash : (!string.IsNullOrWhiteSpace(ApiHash) ? ApiHash : DefaultBuildApiHash);
}
