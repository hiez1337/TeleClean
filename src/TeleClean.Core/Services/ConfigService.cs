using System.Text.Json;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;

namespace TeleClean.Core.Services;

/// <summary>
/// Implements application configuration management and 3-tier credentials resolution.
/// </summary>
public sealed class ConfigService : IConfigService
{
    public const int BuildApiId = 11600115;
    public const string BuildApiHash = "dabf2aadd76c3a56983809d54c0760c6";

    private readonly object _lock = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string? _customDataDir;
    private AppConfig _currentConfig = new();
    private bool _isLoaded;

    public AppConfig CurrentConfig
    {
        get
        {
            lock (_lock)
            {
                if (!_isLoaded)
                {
                    LoadSyncInternal();
                }
                return _currentConfig;
            }
        }
    }

    public ConfigService() : this(null)
    {
    }

    public ConfigService(string? customDataDir)
    {
        _customDataDir = customDataDir;
    }

    private void LoadSyncInternal()
    {
        var path = GetConfigFilePath();
        if (!File.Exists(path))
        {
            _currentConfig = new AppConfig
            {
                SessionPath = Path.Combine(GetDataDirectory(), "teleclean_v2.session"),
                AvatarCacheDir = Path.Combine(GetDataDirectory(), "avatars")
            };
            _isLoaded = true;
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _currentConfig = loaded ?? new AppConfig();
            if (string.IsNullOrWhiteSpace(_currentConfig.SessionPath) ||
                Path.GetFileName(_currentConfig.SessionPath).Equals("teleclean.session", StringComparison.OrdinalIgnoreCase))
            {
                _currentConfig.SessionPath = Path.Combine(GetDataDirectory(), "teleclean_v2.session");
            }
            if (string.IsNullOrWhiteSpace(_currentConfig.AvatarCacheDir))
            {
                _currentConfig.AvatarCacheDir = Path.Combine(GetDataDirectory(), "avatars");
            }
            _isLoaded = true;
        }
        catch
        {
            _currentConfig = new AppConfig
            {
                SessionPath = Path.Combine(GetDataDirectory(), "teleclean_v2.session"),
                AvatarCacheDir = Path.Combine(GetDataDirectory(), "avatars")
            };
            _isLoaded = true;
        }
    }

    public string GetDataDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_customDataDir))
        {
            Directory.CreateDirectory(_customDataDir);
            return _customDataDir;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDataDir = Path.Combine(appData, "TeleClean");
        var appDataSession = Path.Combine(appDataDir, "teleclean_v2.session");
        var appDataConfig = Path.Combine(appDataDir, "config.json");

        // Priority 1: If session or config already exists in %APPDATA%/TeleClean, use it
        if (File.Exists(appDataSession) || File.Exists(appDataConfig))
        {
            return appDataDir;
        }

        // Priority 2: Local .teleclean directory if present (dev / portable mode)
        var localDevDir = Path.Combine(Directory.GetCurrentDirectory(), ".teleclean");
        if (Directory.Exists(localDevDir))
        {
            return localDevDir;
        }

        var baseAppDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".teleclean");
        if (Directory.Exists(baseAppDir))
        {
            return baseAppDir;
        }

        try
        {
            var dirInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dirInfo != null)
            {
                var candidate = Path.Combine(dirInfo.FullName, ".teleclean");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                dirInfo = dirInfo.Parent;
            }
        }
        catch
        {
            // Ignore directory traversal exceptions
        }

        // Priority 3: Standard Windows %APPDATA%/TeleClean
        Directory.CreateDirectory(appDataDir);
        return appDataDir;
    }

    private string GetConfigFilePath() => Path.Combine(GetDataDirectory(), "config.json");

    public async Task<AppConfig> LoadConfigAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try { return CurrentConfig; }
        finally { _fileLock.Release(); }
    }

    public async Task SaveConfigAsync(AppConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var path = GetConfigFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        await _fileLock.WaitAsync(ct);
        try
        {
            await File.WriteAllTextAsync(temporary, json, ct);
            File.Move(temporary, path, overwrite: true);
            lock (_lock)
            {
                _currentConfig = config;
                _isLoaded = true;
            }
        }
        finally
        {
            _fileLock.Release();
            File.Delete(temporary);
        }
    }

    public (int ApiId, string ApiHash) GetEffectiveCredentials()
    {
        // Tier 1: Environment variables
        var envIdStr = Environment.GetEnvironmentVariable("TELEGRAM_API_ID");
        var envHash = Environment.GetEnvironmentVariable("TELEGRAM_API_HASH");

        if (!string.IsNullOrWhiteSpace(envIdStr) &&
            int.TryParse(envIdStr, out var envId) &&
            envId > 0 &&
            !string.IsNullOrWhiteSpace(envHash))
        {
            return (envId, envHash.Trim());
        }

        // Tier 2: Saved config.json
        lock (_lock)
        {
            if (_isLoaded)
            {
                var savedId = _currentConfig.GetEffectiveApiId();
                var savedHash = _currentConfig.GetEffectiveApiHash();

                if (savedId > 0 && !string.IsNullOrWhiteSpace(savedHash) &&
                    (savedId != BuildApiId || savedHash != BuildApiHash))
                {
                    return (savedId, savedHash);
                }
            }
        }

        // Tier 3: Hardcoded fallback
        return (BuildApiId, BuildApiHash);
    }
}
