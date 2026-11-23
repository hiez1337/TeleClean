using CommunityToolkit.Mvvm.Messaging;
using TeleClean.App.Helpers;
using TeleClean.App.Messages;
using TeleClean.App.Models;
using TeleClean.App.Services;
using TeleClean.App.ViewModels;
using TeleClean.Core.Models;
using TeleClean.Core.Services;
using TeleClean.Tests.Mocks;
using Xunit;

namespace TeleClean.Tests;

public sealed class UiRedesignAndHardeningTests
{
    private sealed class InlineDispatcher : IDispatcherService
    {
        public bool HasThreadAccess => true;
        public void Enqueue(Action action) => action();
        public Task EnqueueAsync(Func<Task> function) => function();
    }

    [Theory]
    [InlineData("Архитектура города", "АГ")]
    [InlineData("Дизайн-среда", "ДС")]
    [InlineData("Telegram", "T")]
    [InlineData("  Бюро переводов  ", "БП")]
    [InlineData("★ News / Updates", "NU")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData(null, "?")]
    public void AvatarInitialsHelper_ComputesExpectedInitials(string? title, string expected)
    {
        Assert.Equal(expected, AvatarInitialsHelper.GetInitials(title));
    }

    [Fact]
    public void AvatarInitialsHelper_PaletteIndexIsDeterministicAndInRange()
    {
        int first = AvatarInitialsHelper.GetPaletteIndex("Научный обзор");
        int second = AvatarInitialsHelper.GetPaletteIndex("  научный обзор ");

        Assert.Equal(first, second);
        Assert.InRange(first, 0, AvatarInitialsHelper.PaletteSize - 1);
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("   ", true, null)]
    [InlineData("tg://proxy?server=1.2.3.4&port=443&secret=dd1234", true, "tg://proxy?server=1.2.3.4&port=443&secret=dd1234")]
    [InlineData("https://t.me/proxy?server=proxy.example&port=443&secret=abc", true, "https://t.me/proxy?server=proxy.example&port=443&secret=abc")]
    [InlineData("t.me/proxy?server=1.2.3.4", true, "https://t.me/proxy?server=1.2.3.4")]
    [InlineData("not-a-proxy", false, null)]
    [InlineData("https://example.com/proxy?server=1.2.3.4", false, null)]
    [InlineData("tg://proxy?port=443&secret=abc", false, null)]
    public void ProxyUrlValidator_ValidatesAndNormalizes(string? input, bool expectedOk, string? expectedNormalized)
    {
        bool ok = ProxyUrlValidator.TryValidateAndNormalize(input, out string? normalized, out string? error);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedNormalized, normalized);
        if (expectedOk) Assert.Null(error);
        else Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void DialogItemViewModel_SemanticToneAndCleanStatusStripEmojis()
    {
        var active = new DialogItemViewModel(new DialogItem { Id = 1, Title = "Alpha", IsActive = true, IsJoined = true, UnreadCount = 12_500 });
        Assert.Equal("Активен", active.CleanStatusText);
        Assert.Equal(StatusTone.Success, active.StatusTone);
        Assert.True(active.ShowStatusDot);
        Assert.True(active.HasUnread);
        Assert.Equal("9 999+", active.UnreadBadgeText);

        active.MarkLeft("✅ Вышел");
        Assert.Equal("Вышел", active.CleanStatusText);
        Assert.Equal(StatusTone.Neutral, active.StatusTone);
        Assert.False(active.ShowStatusDot);

        var rateLimited = new DialogItemViewModel(new DialogItem { Id = 2, Title = "Beta", StatusBadge = "⏳ Ограничение" });
        Assert.Equal("Ограничение", rateLimited.CleanStatusText);
        Assert.Equal(StatusTone.Warning, rateLimited.StatusTone);

        var forbidden = new DialogItemViewModel(new DialogItem { Id = 3, Title = "Gamma", StatusBadge = "Нет доступа" });
        Assert.Equal(StatusTone.Danger, forbidden.StatusTone);
    }

    [Fact]
    public async Task DialogListViewModel_ExposesCategoryMetadataSortFlagsAndNotificationState()
    {
        var telegram = new FakeTelegramService { DownloadAvatarHandler = (_, _) => Task.FromResult<string?>(null) };
        telegram.SetDialogs(new[]
        {
            new DialogItem { Id = 1, Title = "Channel A", DialogType = DialogType.Channel },
            new DialogItem { Id = 2, Title = "Chat B", DialogType = DialogType.Group },
            new DialogItem { Id = 3, Title = "Bot C", DialogType = DialogType.Bot }
        });

        using var vm = new DialogListViewModel(telegram, new FakeCacheService(), new InlineDispatcher());
        await vm.LoadDialogsAsync();

        Assert.Equal("Каналы", vm.CurrentCategoryTitle);
        Assert.Equal(1, vm.CurrentCategoryCount);
        Assert.True(vm.HasChannels);
        Assert.True(vm.HasChats);
        Assert.True(vm.HasBots);
        Assert.False(vm.HasDeleted);
        Assert.False(vm.HasNotificationArea);

        Assert.True(vm.IsTitleSorted);
        vm.SortByUnread();
        Assert.True(vm.IsUnreadSorted);
        Assert.False(vm.IsTitleSorted);
        vm.SortBySubscribers();
        Assert.True(vm.IsParticipantsSorted);
        vm.SortByActivity();
        Assert.True(vm.IsActivitySorted);
    }

    [Fact]
    public async Task SettingsViewModel_ValidatesProxyRejectsNaNAndDebouncesSliderSaves()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TeleClean-settings-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configService = new ConfigService(tempDir);
            var cache = new FakeCacheService();
            var telegram = new FakeTelegramService();
            using var vm = new SettingsViewModel(configService, cache, telegram, new InlineDispatcher());
            await vm.InitializeAsync();

            // Invalid proxy rejected without updating HasProxyConfigured
            vm.ProxyUrl = "invalid-url";
            await vm.SaveProxyAsync();
            Assert.True(vm.HasError);
            Assert.False(vm.HasProxyConfigured);

            // Valid proxy normalized and saved
            vm.ProxyUrl = "t.me/proxy?server=127.0.0.1&port=443&secret=dd0000";
            await vm.SaveProxyAsync();
            Assert.False(vm.HasError);
            Assert.True(vm.HasProxyConfigured);
            Assert.Equal("https://t.me/proxy?server=127.0.0.1&port=443&secret=dd0000", vm.ProxyUrl);

            // Clear proxy resets flag
            await vm.ClearProxyAsync();
            Assert.False(vm.HasProxyConfigured);

            // NaN ApiId is rejected cleanly instead of overflowing cast
            vm.ApiId = double.NaN;
            vm.ApiHash = new string('a', 32);
            await vm.SaveApiKeysAsync();
            Assert.True(vm.HasError);

            // Theme and backdrop changes publish messages and persist all 4 backdrop options
            string? observedTheme = null;
            string? observedBackdrop = null;
            var recipient = new object();
            WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(recipient, (_, msg) => observedTheme = msg.Value);
            WeakReferenceMessenger.Default.Register<BackdropChangedMessage>(recipient, (_, msg) => observedBackdrop = msg.Value);
            try
            {
                vm.SelectedThemeIndex = 1; // Light
                Assert.Equal("light", observedTheme);

                vm.SelectedBackdropIndex = 2; // Acrylic
                Assert.Equal("Acrylic", observedBackdrop);

                vm.SelectedBackdropIndex = 3; // Solid
                Assert.Equal("Solid", observedBackdrop);
            }
            finally
            {
                WeakReferenceMessenger.Default.UnregisterAll(recipient);
            }

            // Debounced delay save persists the latest value and Solid backdrop reloads as index 3
            vm.DelayBetweenRequests = 4.5;
            vm.DelayBetweenRequests = 6.5;
            await Task.Delay(400);
            var saved = await configService.LoadConfigAsync();
            Assert.Equal(6.5, saved.DefaultDelaySeconds);
            Assert.Equal("Solid", saved.Backdrop);

            using var reloadedVm = new SettingsViewModel(new ConfigService(tempDir), cache, telegram, new InlineDispatcher());
            await reloadedVm.InitializeAsync();
            Assert.Equal(3, reloadedVm.SelectedBackdropIndex);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
