using TeleClean.App.Services;
using TeleClean.App.ViewModels;
using TeleClean.Core.Models;
using TeleClean.Tests.Mocks;
using Xunit;

namespace TeleClean.Tests;

public sealed class DialogListLifecycleTests
{
    private sealed class InlineDispatcher : IDispatcherService
    {
        public bool HasThreadAccess => true;
        public void Enqueue(Action action) => action();
        public Task EnqueueAsync(Func<Task> function) => function();
    }

    private static TaskCompletionSource<IReadOnlyList<DialogItem>> Response() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task ResetRejectsLateResponseFromPreviousAccount()
    {
        var response = Response();
        CancellationToken requestedToken = default;
        var telegram = new FakeTelegramService { FetchDialogsHandler = ct => { requestedToken = ct; return response.Task; } };
        var cache = new FakeCacheService();
        using var vm = new DialogListViewModel(telegram, cache, new InlineDispatcher());

        var loading = vm.LoadDialogsAsync();
        vm.ResetAccount();
        response.SetResult(new[] { new DialogItem { Id = 1, Title = "Old account" } });
        await loading;

        Assert.True(requestedToken.IsCancellationRequested);
        Assert.Empty(vm.VisibleDialogs);
        Assert.Empty(cache.CachedDialogs);
        Assert.False(vm.HasRequestedDialogs);
        Assert.False(vm.IsBusy);
        Assert.False(vm.CanSelect);
    }

    [Fact]
    public async Task OldCompletionCannotUnlockOrOverwriteNewLoad()
    {
        var oldResponse = Response();
        var newResponse = Response();
        var telegram = new FakeTelegramService { FetchDialogsHandler = _ => oldResponse.Task, DownloadAvatarHandler = (_, _) => Task.FromResult<string?>(null) };
        var cache = new FakeCacheService();
        using var vm = new DialogListViewModel(telegram, cache, new InlineDispatcher());
        var oldLoad = vm.LoadDialogsAsync();
        vm.ResetAccount();
        telegram.FetchDialogsHandler = _ => newResponse.Task;
        var newLoad = vm.LoadDialogsAsync();

        oldResponse.SetResult(new[] { new DialogItem { Id = 1 } });
        await oldLoad;
        Assert.True(vm.IsBusy);
        Assert.Empty(vm.VisibleDialogs);

        newResponse.SetResult(new[] { new DialogItem { Id = 2 } });
        await newLoad;
        Assert.False(vm.IsBusy);
        Assert.True(vm.CanSelect);
        Assert.Equal(2, Assert.Single(vm.VisibleDialogs).Id);
        Assert.Equal(2, Assert.Single(cache.CachedDialogs).Id);
    }

    [Fact]
    public async Task EmptyRemoteResponseReplacesCachedListAndDoesNotReloadOnNavigation()
    {
        var calls = 0;
        var telegram = new FakeTelegramService { FetchDialogsHandler = _ => { calls++; return Task.FromResult<IReadOnlyList<DialogItem>>(Array.Empty<DialogItem>()); } };
        var cache = new FakeCacheService();
        await cache.SaveDialogCacheAsync(new[] { new DialogItem { Id = 1 } });
        using var vm = new DialogListViewModel(telegram, cache, new InlineDispatcher());

        await vm.InitializeAsync();
        await vm.InitializeAsync();

        Assert.Equal(1, calls);
        Assert.Empty(vm.VisibleDialogs);
        Assert.Empty(cache.CachedDialogs);
        Assert.True(vm.ShowEmpty);
    }

    [Fact]
    public async Task FailedRefreshLeavesCacheVisibleButDisablesDestructiveActions()
    {
        var telegram = new FakeTelegramService { FetchDialogsHandler = _ => throw new IOException("Offline") };
        var cache = new FakeCacheService();
        await cache.SaveDialogCacheAsync(new[] { new DialogItem { Id = 1 } });
        using var vm = new DialogListViewModel(telegram, cache, new InlineDispatcher());

        await vm.LoadDialogsAsync();
        vm.SelectAll();

        Assert.Single(vm.VisibleDialogs);
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.CanSelect);
        Assert.False(vm.CanExecuteAction);
        Assert.Empty(vm.GetSelectedItems());
    }

    [Fact]
    public async Task SelectionTracksActualVisibleScopeAndExcludesAlreadyProcessedRows()
    {
        var telegram = new FakeTelegramService { DownloadAvatarHandler = (_, _) => Task.FromResult<string?>(null) };
        telegram.SetDialogs(new[]
        {
            new DialogItem { Id = 1, Title = "Alpha" },
            new DialogItem { Id = 2, Title = "Beta" },
            new DialogItem { Id = 3, Title = "Alpha processed", IsJoined = false },
            new DialogItem { Id = 1, Title = "Alpha bot", DialogType = DialogType.Bot }
        });
        using var vm = new DialogListViewModel(telegram, new FakeCacheService(), new InlineDispatcher());
        await vm.LoadDialogsAsync();
        vm.SelectAll();
        Assert.Equal(2, vm.GetSelectedItems().Count);

        vm.SearchQuery = "Alpha";
        Assert.Equal(1, Assert.Single(vm.GetSelectedItems()).Id);
        Assert.Equal(2, vm.VisibleDialogs.Count);
        vm.ClearFilters();
        Assert.Equal(1, vm.SelectedCount);
        vm.SelectedCategoryIndex = (int)DialogCategoryTab.Bots;
        Assert.Empty(vm.GetSelectedItems());
        vm.SelectAll();
        Assert.Equal(DialogType.Bot, Assert.Single(vm.GetSelectedItems()).DialogType);
    }

    [Fact]
    public async Task DisposeCancelsPendingLoadAndRejectsItsResult()
    {
        var response = Response();
        var telegram = new FakeTelegramService { FetchDialogsHandler = _ => response.Task };
        var cache = new FakeCacheService();
        var vm = new DialogListViewModel(telegram, cache, new InlineDispatcher());
        var loading = vm.LoadDialogsAsync();
        vm.Dispose();
        response.SetResult(new[] { new DialogItem { Id = 1 } });
        await loading;
        Assert.Empty(cache.CachedDialogs);
        Assert.Empty(vm.VisibleDialogs);
        Assert.False(vm.CanRefresh);
    }
}
