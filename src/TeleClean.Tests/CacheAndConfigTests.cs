using System.Text;
using System.Text.Json;
using TeleClean.Core.Models;
using TeleClean.Core.Services;
using TeleClean.Tests.Fixtures;
using TL;
using Xunit;

namespace TeleClean.Tests;

public class CacheAndConfigTests : IDisposable
{
    private readonly string _tempTestDir;

    public CacheAndConfigTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), "TeleCleanTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempTestDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempTestDir))
            {
                Directory.Delete(_tempTestDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task CredentialsResolution_Tier1_EnvironmentVariablesTakePrecedence()
    {
        var configService = new ConfigService(_tempTestDir);
        await configService.LoadConfigAsync();

        var originalId = Environment.GetEnvironmentVariable("TELEGRAM_API_ID");
        var originalHash = Environment.GetEnvironmentVariable("TELEGRAM_API_HASH");

        try
        {
            Environment.SetEnvironmentVariable("TELEGRAM_API_ID", "999999");
            Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", "env_override_hash_xyz");

            var creds = configService.GetEffectiveCredentials();
            Assert.Equal(999999, creds.ApiId);
            Assert.Equal("env_override_hash_xyz", creds.ApiHash);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TELEGRAM_API_ID", originalId);
            Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", originalHash);
        }
    }

    [Fact]
    public async Task CredentialsResolution_Tier2_ConfigFileUsedWhenNoEnvVars()
    {
        var originalId = Environment.GetEnvironmentVariable("TELEGRAM_API_ID");
        var originalHash = Environment.GetEnvironmentVariable("TELEGRAM_API_HASH");

        try
        {
            // Clear environment variables
            Environment.SetEnvironmentVariable("TELEGRAM_API_ID", null);
            Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", null);

            var configService = new ConfigService(_tempTestDir);
            var customConfig = new AppConfig
            {
                ApiId = 777777,
                ApiHash = "config_json_hash_abc"
            };
            await configService.SaveConfigAsync(customConfig);

            var loaded = await configService.LoadConfigAsync();
            Assert.Equal(777777, loaded.ApiId);

            var creds = configService.GetEffectiveCredentials();
            Assert.Equal(777777, creds.ApiId);
            Assert.Equal("config_json_hash_abc", creds.ApiHash);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TELEGRAM_API_ID", originalId);
            Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", originalHash);
        }
    }

    [Fact]
    public async Task CredentialsResolution_Tier2_NestedApiKeysConfigSupported()
    {
        var originalId = Environment.GetEnvironmentVariable("TELEGRAM_API_ID");
        var originalHash = Environment.GetEnvironmentVariable("TELEGRAM_API_HASH");

        try
        {
            Environment.SetEnvironmentVariable("TELEGRAM_API_ID", null);
            Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", null);

            var configService = new ConfigService(_tempTestDir);
            var customConfig = new AppConfig
            {
                ApiKeys = new ApiKeysConfig
                {
                    ApiId = 555555,
                    ApiHash = "nested_api_hash_key"
                }
            };
            await configService.SaveConfigAsync(customConfig);
            await configService.LoadConfigAsync();

            var creds = configService.GetEffectiveCredentials();
            Assert.Equal(555555, creds.ApiId);
            Assert.Equal("nested_api_hash_key", creds.ApiHash);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TELEGRAM_API_ID", originalId);
            Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", originalHash);
        }
    }

    [Fact]
    public async Task CredentialsResolution_Tier3_HardcodedFallbackWhenEmpty()
    {
        var originalId = Environment.GetEnvironmentVariable("TELEGRAM_API_ID");
        var originalHash = Environment.GetEnvironmentVariable("TELEGRAM_API_HASH");

        try
        {
            Environment.SetEnvironmentVariable("TELEGRAM_API_ID", null);
            Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", null);

            var configService = new ConfigService(_tempTestDir);
            await configService.LoadConfigAsync();

            var creds = configService.GetEffectiveCredentials();
            Assert.Equal(11600115, creds.ApiId);
            Assert.Equal("dabf2aadd76c3a56983809d54c0760c6", creds.ApiHash);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TELEGRAM_API_ID", originalId);
            Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", originalHash);
        }
    }

    [Fact]
    public async Task ChannelCache_SaveAndLoadRoundTrip()
    {
        var configService = new ConfigService(_tempTestDir);
        await configService.LoadConfigAsync();
        var cacheService = new CacheService(configService);

        // Cold start - no cache file
        var empty = await cacheService.LoadDialogCacheAsync();
        Assert.Empty(empty);

        var sample = SampleDialogs.CreateExtendedDialogsWithCyrillicAnd64Bit();
        await cacheService.SaveDialogCacheAsync(sample);

        var restored = await cacheService.LoadDialogCacheAsync();
        Assert.Equal(sample.Count, restored.Count);

        for (int i = 0; i < sample.Count; i++)
        {
            Assert.Equal(sample[i].Id, restored[i].Id);
            Assert.Equal(sample[i].AccessHash, restored[i].AccessHash);
            Assert.Equal(sample[i].Title, restored[i].Title);
            Assert.Equal(sample[i].Username, restored[i].Username);
            Assert.Equal(sample[i].ParticipantCount, restored[i].ParticipantCount);
            Assert.Equal(sample[i].UnreadCount, restored[i].UnreadCount);
            Assert.Equal(sample[i].IsChannel, restored[i].IsChannel);
            Assert.Equal(sample[i].IsJoined, restored[i].IsJoined);
            Assert.Equal(sample[i].DialogType, restored[i].DialogType);
        }

        // Clear cache
        await cacheService.ClearCacheAsync();
        var cleared = await cacheService.LoadDialogCacheAsync();
        Assert.Empty(cleared);
    }

    [Fact]
    public async Task LeaveHistory_AppendAndLoad()
    {
        var configService = new ConfigService(_tempTestDir);
        await configService.LoadConfigAsync();
        var cacheService = new CacheService(configService);

        var entry1 = new BulkLogEntry(DateTime.UtcNow, -1001, "Test Channel 1", LeaveResult.Success, "Left successfully");
        var entry2 = new BulkLogEntry(DateTime.UtcNow, -2002, "Test Group 2", LeaveResult.RateLimited, "Flood wait 30s");

        await cacheService.AppendHistoryAsync(entry1);
        await cacheService.AppendHistoryAsync(entry2);

        var history = await cacheService.LoadHistoryAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(-1001, history[0].DialogId);
        Assert.Equal(LeaveResult.Success, history[0].Result);
        Assert.Equal(-2002, history[1].DialogId);
        Assert.Equal(LeaveResult.RateLimited, history[1].Result);
    }

    [Fact]
    public async Task LeaveHistory_LegacyPythonFormatMigration()
    {
        var configService = new ConfigService(_tempTestDir);
        await configService.LoadConfigAsync();
        var cacheService = new CacheService(configService);

        // Write legacy Python leave_history.json format
        var historyPath = Path.Combine(_tempTestDir, "leave_history.json");
        var legacyJson = """
        [
          {
            "started_at": 1725570000.0,
            "total": 3,
            "completed": 3,
            "success": 2,
            "errors": [
              {
                "channel_id": -1001999999,
                "title": "Blocked Group",
                "error": "rate_limited"
              }
            ]
          }
        ]
        """;
        await File.WriteAllTextAsync(historyPath, legacyJson);

        var loaded = await cacheService.LoadHistoryAsync();
        Assert.NotEmpty(loaded);

        // Verifies rate_limited entry mapped
        var rateLimitedEntry = loaded.FirstOrDefault(e => e.DialogId == -1001999999);
        Assert.NotNull(rateLimitedEntry);
        Assert.Equal("Blocked Group", rateLimitedEntry.Title);
        Assert.Equal(LeaveResult.RateLimited, rateLimitedEntry.Result);

        // Verifies session summary entry mapped
        var sessionSummary = loaded.FirstOrDefault(e => e.Title == "Сессия");
        Assert.NotNull(sessionSummary);
        Assert.Contains("Всего: 3", sessionSummary.Message);
        Assert.Contains("Успешно: 2", sessionSummary.Message);
    }

    [Fact]
    public async Task AvatarCache_JpegAndPngIntegrityValidation()
    {
        var configService = new ConfigService(_tempTestDir);
        await configService.LoadConfigAsync();
        var cacheService = new CacheService(configService);

        long jpegId = -1001;
        long pngId = -2002;
        long corruptShortId = -3003;
        long corruptHeaderId = -4004;

        // 1. Valid JPEG: FF D8 FF + padding to >= 16 bytes
        var validJpegBytes = new byte[32];
        validJpegBytes[0] = 0xFF;
        validJpegBytes[1] = 0xD8;
        validJpegBytes[2] = 0xFF;
        validJpegBytes[3] = 0xE0;
        using (var stream = new MemoryStream(validJpegBytes))
        {
            var path = await cacheService.SaveAvatarAsync(jpegId, stream);
            Assert.True(File.Exists(path));
            Assert.True(cacheService.HasAvatar(jpegId));
        }

        // 2. Valid PNG: 89 50 4E 47 + padding to >= 16 bytes
        var validPngBytes = new byte[32];
        validPngBytes[0] = 0x89;
        validPngBytes[1] = 0x50;
        validPngBytes[2] = 0x4E;
        validPngBytes[3] = 0x47;
        using (var stream = new MemoryStream(validPngBytes))
        {
            var path = await cacheService.SaveAvatarAsync(pngId, stream);
            Assert.True(File.Exists(path));
            Assert.True(cacheService.HasAvatar(pngId));
        }

        // 3. Corrupt file (< 16 bytes): write directly to avatar path
        var shortBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var shortPath = cacheService.GetAvatarPath(corruptShortId);
        Directory.CreateDirectory(Path.GetDirectoryName(shortPath)!);
        await File.WriteAllBytesAsync(shortPath, shortBytes);
        Assert.True(File.Exists(shortPath));

        // HasAvatar must detect corruption, delete the file, and return false
        Assert.False(cacheService.HasAvatar(corruptShortId));
        Assert.False(File.Exists(shortPath));

        // 4. Corrupt header (>= 16 bytes but not JPEG/PNG magic bytes)
        var invalidHeaderBytes = Encoding.UTF8.GetBytes("THIS_IS_NOT_A_VALID_IMAGE_FILE_CONTENTS_123");
        var invalidPath = cacheService.GetAvatarPath(corruptHeaderId);
        await File.WriteAllBytesAsync(invalidPath, invalidHeaderBytes);
        Assert.True(File.Exists(invalidPath));

        // HasAvatar must detect invalid header, delete the file, and return false
        Assert.False(cacheService.HasAvatar(corruptHeaderId));
        Assert.False(File.Exists(invalidPath));

        // 5. Non-existent file returns false
        Assert.False(cacheService.HasAvatar(-999999));

        // 6. SaveAvatarAsync with invalid stream throws InvalidOperationException
        using (var invalidStream = new MemoryStream(invalidHeaderBytes))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cacheService.SaveAvatarAsync(-5005, invalidStream));
        }
    }

    [Fact]
    public async Task TelegramService_UnconfiguredState_ThrowsOrReturnsExpected()
    {
        var configService = new ConfigService(_tempTestDir);
        var cacheService = new CacheService(configService);
        await using var telegramService = new TelegramService(configService, cacheService);

        Assert.False(telegramService.IsAuthorized);
        await Assert.ThrowsAsync<ArgumentNullException>(() => telegramService.BeginLoginWithQrAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => telegramService.LoginWithPhoneAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => telegramService.SubmitPhoneCodeAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => telegramService.SubmitPasswordAsync(""));
    }

    [Fact]
    public async Task TelegramService_CheckExistingSessionAsync_ReturnsFalseSafelyWhenNoSession()
    {
        var configService = new ConfigService(_tempTestDir);
        var cacheService = new CacheService(configService);
        await using var telegramService = new TelegramService(configService, cacheService);
        bool hasSession = await telegramService.CheckExistingSessionAsync();
        Assert.False(hasSession);
        Assert.False(telegramService.IsAuthorized);
    }

    [Fact]
    public async Task AppConfig_Backdrop_DefaultsToMicaAndPersists()
    {
        var configService = new ConfigService(_tempTestDir);
        var initialConfig = await configService.LoadConfigAsync();
        Assert.Equal("Mica", initialConfig.Backdrop);

        initialConfig.Backdrop = "Acrylic";
        await configService.SaveConfigAsync(initialConfig);

        var reloaded = await configService.LoadConfigAsync();
        Assert.Equal("Acrylic", reloaded.Backdrop);
    }
}
