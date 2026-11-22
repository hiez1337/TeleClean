using System.Reflection;
using TeleClean.Core.Models;
using TeleClean.Core.Services;
using TeleClean.Tests.Mocks;
using TL;
using WTelegram;
using Xunit;

namespace TeleClean.Tests;

public sealed class SessionPersistenceRegressionTests
{
    [Fact]
    public async Task QrAuthorizationSurvivesDisposalAndReopeningSameSession()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeleClean-session-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = new ConfigService(directory);
            await config.SaveConfigAsync(new AppConfig { ApiId = 12345, ApiHash = new string('1', 32) });
            var service = new TelegramService(config, new FakeCacheService());
            var create = typeof(TelegramService).GetMethod("EnsureClientCreated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var clientField = typeof(TelegramService).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!;
            create.Invoke(service, null);
            var client = (Client)clientField.GetValue(service)!;

            // Exercise WTelegram's actual encrypted store without connecting to Telegram.
            client.LoginAlreadyDone(new Auth_Authorization { user = new User { id = 987654321, first_name = "Offline" } });
            var path = service.GetSessionPath();
            await service.DisposeAsync();
            Assert.True(new FileInfo(path).Length > 0);

            await using var reopened = new TelegramService(new ConfigService(directory), new FakeCacheService());
            create.Invoke(reopened, null);
            var restored = (Client)clientField.GetValue(reopened)!;
            Assert.Equal(987654321, restored.UserId);
            var callback = typeof(TelegramService).GetMethod("ConfigCallback", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal("-1", callback.Invoke(reopened, new object[] { "user_id" }));
            Assert.Equal(path, reopened.GetSessionPath());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RelativeSessionPathIsAnchoredToProfileAcrossClientRecreation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeleClean-profile-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = new ConfigService(directory);
            await config.SaveConfigAsync(new AppConfig { SessionPath = "saved.session" });
            await using var service = new TelegramService(config, new FakeCacheService());
            Assert.Equal(Path.Combine(directory, "saved.session"), service.GetSessionPath());
            await service.RecreateClientAsync();
            Assert.Equal(Path.Combine(directory, "saved.session"), service.GetSessionPath());
            await using var reopened = new TelegramService(new ConfigService(directory), new FakeCacheService());
            Assert.Equal(service.GetSessionPath(), reopened.GetSessionPath());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
