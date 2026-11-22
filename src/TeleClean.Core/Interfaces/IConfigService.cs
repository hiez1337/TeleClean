using TeleClean.Core.Models;

namespace TeleClean.Core.Interfaces;

/// <summary>
/// Service abstraction for loading and persisting configuration with 3-tier credentials fallback.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// The currently active in-memory configuration.
    /// </summary>
    AppConfig CurrentConfig { get; }

    /// <summary>
    /// Loads configuration from disk (config.json) or returns defaults.
    /// </summary>
    Task<AppConfig> LoadConfigAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists configuration to disk (config.json).
    /// </summary>
    Task SaveConfigAsync(AppConfig config, CancellationToken ct = default);

    /// <summary>
    /// Resolves effective Telegram credentials applying 3-tier fallback:
    /// Tier 1: Environment variables (TELEGRAM_API_ID, TELEGRAM_API_HASH)
    /// Tier 2: Saved config.json
    /// Tier 3: Hardcoded defaults (11600115, dabf2aadd76c3a56983809d54c0760c6)
    /// </summary>
    (int ApiId, string ApiHash) GetEffectiveCredentials();

    /// <summary>
    /// Resolves the application data directory (%APPDATA%/TeleClean or .teleclean).
    /// </summary>
    string GetDataDirectory();
}
