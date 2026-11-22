using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;
using TeleClean.Core.Services;
using TeleClean.Tests.Mocks;
using Xunit;

namespace TeleClean.Tests;

public class AuthFlowAndSessionTests
{
    private class FakeConfigService : IConfigService
    {
        public AppConfig CurrentConfig { get; set; } = new();
        public string DataDir { get; set; } = string.Empty;

        public (int ApiId, string ApiHash) GetEffectiveCredentials() => (12345, "test_hash");
        public string GetDataDirectory() => DataDir;
        public Task<AppConfig> LoadConfigAsync(CancellationToken ct = default) => Task.FromResult(CurrentConfig);
        public Task SaveConfigAsync(AppConfig config, CancellationToken ct = default)
        {
            CurrentConfig = config;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CheckExistingSessionAsync_WhenNoSessionFile_ReturnsFalsePromptly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "teleclean_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new FakeConfigService
            {
                DataDir = tempDir,
                CurrentConfig = new AppConfig
                {
                    SessionPath = Path.Combine(tempDir, "non_existent.session")
                }
            };
            var cache = new FakeCacheService();
            var service = new TelegramService(config, cache);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            bool hasSession = await service.CheckExistingSessionAsync(cts.Token);

            Assert.False(hasSession);

            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubmitPasswordAsync_EmptyOrWhitespace_ThrowsArgumentException(string password)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "teleclean_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new FakeConfigService { DataDir = tempDir };
            var cache = new FakeCacheService();
            var service = new TelegramService(config, cache);

            await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitPasswordAsync(password));

            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public async Task ResetSessionAsync_CancelsPendingPasswordTcs_WithoutHanging()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "teleclean_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new FakeConfigService
            {
                DataDir = tempDir,
                CurrentConfig = new AppConfig
                {
                    SessionPath = Path.Combine(tempDir, "test.session")
                }
            };
            var cache = new FakeCacheService();
            var service = new TelegramService(config, cache);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await service.ResetSessionAsync(cts.Token);

            Assert.False(service.IsAuthorized);

            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public async Task RecreateClientAsync_CleansUpWithoutThrowing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "teleclean_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new FakeConfigService
            {
                DataDir = tempDir,
                CurrentConfig = new AppConfig
                {
                    SessionPath = Path.Combine(tempDir, "test.session")
                }
            };
            var cache = new FakeCacheService();
            var service = new TelegramService(config, cache);

            await service.RecreateClientAsync();
            Assert.False(service.IsAuthorized);

            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public async Task CheckExistingSessionAsync_WhenEmptySessionFile_ReturnsFalsePromptly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "teleclean_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sessionFile = Path.Combine(tempDir, "empty.session");
            await File.WriteAllBytesAsync(sessionFile, Array.Empty<byte>());

            var config = new FakeConfigService
            {
                DataDir = tempDir,
                CurrentConfig = new AppConfig
                {
                    SessionPath = sessionFile
                }
            };
            var cache = new FakeCacheService();
            var service = new TelegramService(config, cache);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            bool hasSession = await service.CheckExistingSessionAsync(cts.Token);

            Assert.False(hasSession);

            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public void TelegramService_GetSessionPath_ReturnsDeterministicPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "teleclean_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new FakeConfigService
            {
                DataDir = tempDir,
                CurrentConfig = new AppConfig()
            };
            var cache = new FakeCacheService();
            var service = new TelegramService(config, cache);

            var path1 = service.GetSessionPath();
            var path2 = service.GetSessionPath();

            Assert.Equal(path1, path2);
            Assert.EndsWith("teleclean_v2.session", path1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public void ConfigService_CurrentConfig_LoadsSynchronouslyWhenAccessed()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "teleclean_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "config.json");
            File.WriteAllText(configPath, @"{
                ""api_id"": 99999,
                ""api_hash"": ""hash99999"",
                ""theme"": ""light"",
                ""proxy_url"": ""tg://proxy?server=1.2.3.4""
            }");

            var configService = new ConfigService(tempDir);
            var loaded = configService.CurrentConfig;

            Assert.Equal(99999, loaded.ApiId);
            Assert.Equal("hash99999", loaded.ApiHash);
            Assert.Equal("light", loaded.Theme);
            Assert.Equal("tg://proxy?server=1.2.3.4", loaded.ProxyUrl);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public void TelegramService_GetSessionPath_PrioritizesExistingFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "teleclean_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var existingSession = Path.Combine(tempDir, "existing_custom.session");
            File.WriteAllBytes(existingSession, new byte[] { 1, 2, 3 });

            var config = new FakeConfigService
            {
                DataDir = tempDir,
                CurrentConfig = new AppConfig
                {
                    SessionPath = existingSession
                }
            };
            var cache = new FakeCacheService();
            var service = new TelegramService(config, cache);

            Assert.Equal(existingSession, service.GetSessionPath());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }
}
