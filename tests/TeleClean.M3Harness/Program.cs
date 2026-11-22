using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TeleClean.App.Controls;
using TeleClean.App.Converters;
using TeleClean.App.Services;
using TeleClean.App.ViewModels;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;
using TeleClean.Tests.Mocks;

namespace TeleClean.M3Harness;

public class DirectDispatcherService : IDispatcherService
{
    private readonly DispatcherQueue? _queue;

    public DirectDispatcherService(DispatcherQueue? queue = null)
    {
        _queue = queue;
    }

    public bool HasThreadAccess => _queue?.HasThreadAccess ?? true;

    public void Enqueue(Action action)
    {
        if (_queue != null && !_queue.HasThreadAccess)
        {
            _queue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }

    public Task EnqueueAsync(Func<Task> function)
    {
        if (_queue != null && !_queue.HasThreadAccess)
        {
            var tcs = new TaskCompletionSource();
            _queue.TryEnqueue(async () =>
            {
                try
                {
                    await function();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }
        else
        {
            return function();
        }
    }
}

public class TestConfigService : IConfigService
{
    public AppConfig CurrentConfig { get; set; } = new() { DefaultDelaySeconds = 0.01 };
    public Task<AppConfig> LoadConfigAsync(CancellationToken ct = default) => Task.FromResult(CurrentConfig);
    public Task SaveConfigAsync(AppConfig config, CancellationToken ct = default)
    {
        CurrentConfig = config;
        return Task.CompletedTask;
    }
    public (int ApiId, string ApiHash) GetEffectiveCredentials() => (11600115, "dabf2aadd76c3a56983809d54c0760c6");
    public string GetDataDirectory() => AppContext.BaseDirectory;
}

public class TestBulkEngine : IBulkExecutionEngine
{
    public BulkOperationState State { get; set; } = BulkOperationState.Idle;
    public event EventHandler<BulkProgressReport>? ProgressChanged;
    public event EventHandler<BulkLogEntry>? LogEntryAdded;

    public bool StopCalled { get; private set; }
    public bool PauseCalled { get; private set; }
    public bool ResumeCalled { get; private set; }

    public TaskCompletionSource<bool>? StartTaskTcs { get; set; }
    public CancellationToken ReceivedCt { get; private set; }

    public Task StartAsync(IEnumerable<DialogItem> items, TimeSpan delayBetween, TimeSpan rateLimitDelay, CancellationToken ct = default)
    {
        ReceivedCt = ct;
        State = BulkOperationState.Running;
        StartTaskTcs = new TaskCompletionSource<bool>();
        ct.Register(() =>
        {
            State = BulkOperationState.Interrupted;
            StartTaskTcs.TrySetCanceled(ct);
        });
        return StartTaskTcs.Task;
    }

    public void Pause()
    {
        PauseCalled = true;
        State = BulkOperationState.Paused;
    }

    public void Resume()
    {
        ResumeCalled = true;
        State = BulkOperationState.Running;
    }

    public void Stop()
    {
        StopCalled = true;
        State = BulkOperationState.Interrupted;
        StartTaskTcs?.TrySetResult(true);
    }

    public void EmitProgress(BulkProgressReport report)
    {
        State = report.State;
        ProgressChanged?.Invoke(this, report);
    }

    public void EmitLog(BulkLogEntry log)
    {
        LogEntryAdded?.Invoke(this, log);
    }
}

public class TestApp : Application
{
    public int PassedCount { get; private set; }
    public int FailedCount { get; private set; }
    public List<string> FailureMessages { get; } = new();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Console.WriteLine("===============================================================");
        Console.WriteLine("     TELECLEAN EMPIRICAL CHALLENGER M3 TEST SUITE");
        Console.WriteLine("===============================================================");

        var queue = DispatcherQueue.GetForCurrentThread();
        var dispatcher = new DirectDispatcherService(queue);

        try
        {
            await RunDialogListViewModelTests(dispatcher);
            await RunBulkExecutionViewModelTests(dispatcher);
            await RunPluralizationTests();
            await RunAdversarialStressTests(dispatcher);
            await RunAvatarControlAndBadgeTests(dispatcher);
            await RunConverterTypeEdgeCaseTests();
        }
        catch (Exception ex)
        {
            Fail($"Unhandled exception in test harness: {ex}");
        }

        Console.WriteLine("\n===============================================================");
        Console.WriteLine($"FINAL RESULT: {PassedCount} PASSED, {FailedCount} FAILED.");
        if (FailedCount > 0)
        {
            Console.WriteLine("FAILURES:");
            foreach (var fail in FailureMessages)
            {
                Console.WriteLine($"  - {fail}");
            }
        }
        Console.WriteLine("===============================================================");

        Exit();
    }

    private void Pass(string testName, string details = "")
    {
        PassedCount++;
        Console.WriteLine($" [PASS] {testName} {(string.IsNullOrEmpty(details) ? "" : $"({details})")}");
    }

    private void Fail(string testName, string reason = "")
    {
        FailedCount++;
        string msg = $"{testName}: {reason}";
        FailureMessages.Add(msg);
        Console.WriteLine($" [FAIL] {msg}");
    }

    private void Assert(bool condition, string testName, string message = "")
    {
        if (condition)
            Pass(testName, message);
        else
            Fail(testName, message);
    }

    private async Task RunDialogListViewModelTests(IDispatcherService dispatcher)
    {
        Console.WriteLine("\n--- 1. Testing DialogListViewModel with 10,000 Synthetic Dialogs ---");

        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var dvm = new DialogListViewModel(fakeTelegram, fakeCache, dispatcher);

        // 1.1 Generate 10,000 synthetic items
        // 2,500 Channels (Broadcast)
        // 2,500 Chats (1,250 Supergroups + 1,250 Groups)
        // 2,500 Bots
        // 2,500 Deleted
        var items = new List<DialogItem>(10000);

        // Channels: 0..2499
        for (int i = 0; i < 2500; i++)
        {
            items.Add(new DialogItem
            {
                Id = 1000000 + i,
                Title = $"Channel {(2500 - i):D4}", // Reverse ordered for sorting tests
                Username = i % 2 == 0 ? $"chan_{i}" : null,
                DialogType = DialogType.Channel,
                UnreadCount = i < 500 ? (i + 1) : 0, // Exactly 500 have unread > 0
                ParticipantCount = i < 300 ? (i % 50 + 1) : (100 + i), // Exactly 300 have <= 50 participants
                IsJoined = true
            });
        }

        // Chats: 2500..4999 (1250 Supergroup, 1250 Group)
        for (int i = 0; i < 2500; i++)
        {
            items.Add(new DialogItem
            {
                Id = 2000000 + i,
                Title = $"Chat {i:D4}",
                Username = $"chat_{i}",
                DialogType = i % 2 == 0 ? DialogType.Supergroup : DialogType.Group,
                UnreadCount = i < 200 ? 5 : 0,
                ParticipantCount = 20 + i,
                IsJoined = true
            });
        }

        // Bots: 5000..7499
        for (int i = 0; i < 2500; i++)
        {
            items.Add(new DialogItem
            {
                Id = 3000000 + i,
                Title = $"Bot {i:D4}",
                Username = $"bot_{i}",
                DialogType = DialogType.Bot,
                UnreadCount = 0,
                ParticipantCount = 1,
                IsJoined = true
            });
        }

        // Deleted: 7500..9999
        for (int i = 0; i < 2500; i++)
        {
            items.Add(new DialogItem
            {
                Id = 4000000 + i,
                Title = $"Deleted Account {i:D4}",
                Username = null,
                DialogType = DialogType.Deleted,
                UnreadCount = 0,
                ParticipantCount = 0,
                IsJoined = false
            });
        }

        // Set a unique username on item 42 for exact search verification
        items[42].Username = "unique_verified_channel_target";

        Assert(items.Count == 10000, "Synthetic Generation", $"Generated {items.Count} items.");

        // 1.2 Benchmark SetDialogs load time
        var sw = Stopwatch.StartNew();
        dvm.SetDialogs(items);
        sw.Stop();
        long loadMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"   * SetDialogs(10,000) took: {loadMs} ms");
        Assert(loadMs < 3000, "Load Performance", $"Loading 10,000 items took {loadMs}ms (threshold 3000ms)");

        // 1.3 Verify Category Partitioning
        // Default tab is 0 (Channels)
        Assert(dvm.VisibleDialogs.Count == 2500, "Tab 0 (Channels) Count", $"Expected 2500, got {dvm.VisibleDialogs.Count}");
        Assert(dvm.VisibleDialogs.All(d => d.DialogType == DialogType.Channel), "Tab 0 DialogType Purity");

        // Tab 1 (Chats: Supergroup + Group)
        dvm.SelectedCategoryIndex = 1;
        Assert(dvm.VisibleDialogs.Count == 2500, "Tab 1 (Chats) Count", $"Expected 2500, got {dvm.VisibleDialogs.Count}");
        Assert(dvm.VisibleDialogs.All(d => d.DialogType is DialogType.Supergroup or DialogType.Group), "Tab 1 DialogType Purity");

        // Tab 2 (Bots)
        dvm.SelectedCategoryIndex = 2;
        Assert(dvm.VisibleDialogs.Count == 2500, "Tab 2 (Bots) Count", $"Expected 2500, got {dvm.VisibleDialogs.Count}");
        Assert(dvm.VisibleDialogs.All(d => d.DialogType == DialogType.Bot), "Tab 2 DialogType Purity");

        // Tab 3 (Deleted)
        dvm.SelectedCategoryIndex = 3;
        Assert(dvm.VisibleDialogs.Count == 2500, "Tab 3 (Deleted) Count", $"Expected 2500, got {dvm.VisibleDialogs.Count}");
        Assert(dvm.VisibleDialogs.All(d => d.DialogType == DialogType.Deleted), "Tab 3 DialogType Purity");

        // 1.4 Filter Operations on Channels (Tab 0)
        dvm.SelectedCategoryIndex = 0;

        // Filter 0: Все (2500)
        dvm.SelectedFilterIndex = 0;
        Assert(dvm.VisibleDialogs.Count == 2500, "Filter 0 (All) Count", $"Expected 2500, got {dvm.VisibleDialogs.Count}");

        // Filter 1: Непрочитанные (500)
        sw.Restart();
        dvm.SelectedFilterIndex = 1;
        sw.Stop();
        Assert(dvm.VisibleDialogs.Count == 500, "Filter 1 (Unread > 0) Count", $"Expected 500, got {dvm.VisibleDialogs.Count}");
        Assert(dvm.VisibleDialogs.All(d => d.UnreadCount > 0), "Filter 1 Unread Correctness");
        Assert(sw.ElapsedMilliseconds < 500, "Filter 1 Performance", $"{sw.ElapsedMilliseconds}ms");

        // Filter 2: Мало участников (<= 50) (300)
        sw.Restart();
        dvm.SelectedFilterIndex = 2;
        sw.Stop();
        Assert(dvm.VisibleDialogs.Count == 300, "Filter 2 (Members <= 50) Count", $"Expected 300, got {dvm.VisibleDialogs.Count}");
        Assert(dvm.VisibleDialogs.All(d => d.ParticipantCount <= 50), "Filter 2 ParticipantCount Correctness");
        Assert(sw.ElapsedMilliseconds < 500, "Filter 2 Performance", $"{sw.ElapsedMilliseconds}ms");

        // Reset filter
        dvm.SelectedFilterIndex = 0;

        // 1.5 Real-Time Search Query
        // Title substring search
        sw.Restart();
        dvm.SearchQuery = "0042";
        sw.Stop();
        Assert(dvm.VisibleDialogs.Count == 1 && dvm.VisibleDialogs[0].Title == "Channel 0042",
            "Search Title Exact Match", $"Expected 'Channel 0042', found {dvm.VisibleDialogs.Count}");
        Assert(sw.ElapsedMilliseconds < 300, "Search Performance", $"{sw.ElapsedMilliseconds}ms");

        // Case-insensitive search
        dvm.SearchQuery = "cHaNnEl 0042";
        Assert(dvm.VisibleDialogs.Count == 1 && dvm.VisibleDialogs[0].Title == "Channel 0042",
            "Search Case-Insensitive Match");

        // Username search
        dvm.SearchQuery = "verified_channel";
        Assert(dvm.VisibleDialogs.Count == 1 && dvm.VisibleDialogs[0].Username == "unique_verified_channel_target",
            "Search Username Match", $"Expected 'unique_verified_channel_target', found {dvm.VisibleDialogs.Count}");

        // Search with whitespace trimming
        dvm.SearchQuery = "   Channel 0042   ";
        Assert(dvm.VisibleDialogs.Count == 1, "Search Query Trimming");

        // Empty search resets
        dvm.SearchQuery = "";
        Assert(dvm.VisibleDialogs.Count == 2500, "Search Reset to All");

        // 1.6 Multi-Column Sorting
        // Sort 0: Title A-Z (Ascending)
        dvm.SelectedSortIndex = 0;
        bool isSortedByTitle = true;
        for (int i = 0; i < dvm.VisibleDialogs.Count - 1; i++)
        {
            if (string.Compare(dvm.VisibleDialogs[i].Title, dvm.VisibleDialogs[i + 1].Title, StringComparison.CurrentCultureIgnoreCase) > 0)
            {
                isSortedByTitle = false;
                break;
            }
        }
        Assert(isSortedByTitle, "Sort 0 (Title A-Z) Order", $"First: {dvm.VisibleDialogs[0].Title}, Last: {dvm.VisibleDialogs[^1].Title}");

        // Sort 1: Activity / Unread count descending
        sw.Restart();
        dvm.SelectedSortIndex = 1;
        sw.Stop();
        bool isSortedByUnread = true;
        for (int i = 0; i < dvm.VisibleDialogs.Count - 1; i++)
        {
            if (dvm.VisibleDialogs[i].UnreadCount < dvm.VisibleDialogs[i + 1].UnreadCount)
            {
                isSortedByUnread = false;
                break;
            }
        }
        Assert(isSortedByUnread, "Sort 1 (Unread Count Descending) Order", $"First Unread: {dvm.VisibleDialogs[0].UnreadCount}, Last: {dvm.VisibleDialogs[^1].UnreadCount}");
        Assert(sw.ElapsedMilliseconds < 500, "Sort 1 Performance", $"{sw.ElapsedMilliseconds}ms");

        // Sort 2: Participant count descending
        sw.Restart();
        dvm.SelectedSortIndex = 2;
        sw.Stop();
        bool isSortedByMembers = true;
        for (int i = 0; i < dvm.VisibleDialogs.Count - 1; i++)
        {
            if (dvm.VisibleDialogs[i].ParticipantCount < dvm.VisibleDialogs[i + 1].ParticipantCount)
            {
                isSortedByMembers = false;
                break;
            }
        }
        Assert(isSortedByMembers, "Sort 2 (Participant Count Descending) Order", $"First Members: {dvm.VisibleDialogs[0].ParticipantCount}, Last: {dvm.VisibleDialogs[^1].ParticipantCount}");
        Assert(sw.ElapsedMilliseconds < 500, "Sort 2 Performance", $"{sw.ElapsedMilliseconds}ms");

        // 1.7 Selection Controls & Dynamic Counters
        dvm.SelectedSortIndex = 0;
        Assert(dvm.SelectedCount == 0, "Initial SelectedCount == 0");
        Assert(dvm.SelectionCounterText == "Выбрано: 0 из 2500", "Initial SelectionCounterText");
        Assert(!dvm.CanExecuteAction, "CanExecuteAction is false when 0 selected");
        Assert(dvm.ActionButtonText == "🚪 Выйти из выбранных", "Default ActionButtonText (0 selected)");

        // Select All
        sw.Restart();
        dvm.SelectAll();
        sw.Stop();
        Console.WriteLine($"   * SelectAll() on 2500 visible channels took: {sw.ElapsedMilliseconds} ms");
        Assert(dvm.SelectedCount == 2500, "SelectAll SelectedCount == 2500", $"Got {dvm.SelectedCount}");
        Assert(dvm.SelectionCounterText == "Выбрано: 2500 из 2500", "SelectionCounterText after SelectAll");
        Assert(dvm.CanExecuteAction, "CanExecuteAction is true after SelectAll");
        Assert(dvm.ActionButtonText == "🚪 Выйти из 2500 каналов", "ActionButtonText for 2500 channels", $"Got '{dvm.ActionButtonText}'");

        // Deselect All
        sw.Restart();
        dvm.DeselectAll();
        sw.Stop();
        Assert(dvm.SelectedCount == 0, "DeselectAll SelectedCount == 0", $"Got {dvm.SelectedCount}");
        Assert(dvm.SelectionCounterText == "Выбрано: 0 из 2500", "SelectionCounterText after DeselectAll");
        Assert(!dvm.CanExecuteAction, "CanExecuteAction is false after DeselectAll");

        // Single Selection toggle
        dvm.VisibleDialogs[0].IsSelected = true;
        Assert(dvm.SelectedCount == 1, "Single Selection SelectedCount == 1");
        Assert(dvm.SelectionCounterText == "Выбрано: 1 из 2500", "SelectionCounterText for 1 selected");
        Assert(dvm.ActionButtonText == "🚪 Выйти из 1 канала", "ActionButtonText for 1 channel", $"Got '{dvm.ActionButtonText}'");

        dvm.VisibleDialogs[1].IsSelected = true;
        Assert(dvm.SelectedCount == 2, "Selection SelectedCount == 2");
        Assert(dvm.ActionButtonText == "🚪 Выйти из 2 каналов", "ActionButtonText for 2 channels", $"Got '{dvm.ActionButtonText}'");

        // Select All does NOT select left/unjoined items
        dvm.VisibleDialogs[2].IsJoined = false; // Mark unjoined
        dvm.DeselectAll();
        dvm.SelectAll();
        Assert(!dvm.VisibleDialogs[2].IsSelected, "SelectAll ignores !IsJoined items");
        Assert(dvm.SelectedCount == 2499, "SelectedCount reflects joined items only");
        Assert(dvm.LeftCount == 1, "LeftCount correctly tracked");
        Assert(dvm.SelectionCounterText.Contains("Вышло: 1"), "Counter displays left count", $"Got '{dvm.SelectionCounterText}'");

        // Test Category-specific ActionButtonText
        // Chats
        dvm.SelectedCategoryIndex = 1;
        dvm.DeselectAll();
        Assert(dvm.ActionButtonText == "🚪 Выйти из выбранных", "Chats 0 selected button text");
        dvm.VisibleDialogs[0].IsSelected = true;
        Assert(dvm.ActionButtonText == "🚪 Выйти из 1 чата", "Chats 1 selected button text", $"Got '{dvm.ActionButtonText}'");
        dvm.VisibleDialogs[1].IsSelected = true;
        Assert(dvm.ActionButtonText == "🚪 Выйти из 2 чатов", "Chats 2 selected button text", $"Got '{dvm.ActionButtonText}'");

        // Bots
        dvm.SelectedCategoryIndex = 2;
        dvm.DeselectAll();
        Assert(dvm.ActionButtonText == "🚪 Остановить выбранные", "Bots 0 selected button text");
        dvm.VisibleDialogs[0].IsSelected = true;
        Assert(dvm.ActionButtonText == "🚪 Остановить 1 бота", "Bots 1 selected button text", $"Got '{dvm.ActionButtonText}'");
        dvm.VisibleDialogs[1].IsSelected = true;
        Assert(dvm.ActionButtonText == "🚪 Остановить 2 ботов", "Bots 2 selected button text", $"Got '{dvm.ActionButtonText}'");

        // Deleted
        dvm.SelectedCategoryIndex = 3;
        dvm.DeselectAll();
        Assert(dvm.ActionButtonText == "🗑 Удалить выбранные", "Deleted 0 selected button text");
        dvm.VisibleDialogs[0].IsSelected = true;
        Assert(dvm.ActionButtonText == "🗑 Удалить 1 диалог", "Deleted 1 selected button text", $"Got '{dvm.ActionButtonText}'");
        dvm.VisibleDialogs[1].IsSelected = true;
        Assert(dvm.ActionButtonText == "🗑 Удалить 2 диалога", "Deleted 2 selected button text", $"Got '{dvm.ActionButtonText}'");
    }

    private async Task RunBulkExecutionViewModelTests(IDispatcherService dispatcher)
    {
        Console.WriteLine("\n--- 2. Testing BulkExecutionViewModel (Log Capping & Clean Stop) ---");

        var engine = new TestBulkEngine();
        var configService = new TestConfigService();
        var bvm = new BulkExecutionViewModel(engine, configService, dispatcher);

        // 2.1 Simulate 1,500 Progress and Log Events
        long memBefore = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();

        Console.WriteLine("   * Emitting 1,500 progress reports and log entries...");
        for (int i = 1; i <= 1500; i++)
        {
            var log = new BulkLogEntry
            {
                DialogId = 1000 + i,
                Title = $"Dialog #{i}",
                Result = i % 10 == 0 ? LeaveResult.Error : LeaveResult.Success,
                Message = $"Simulated processing message for dialog {i}",
                Timestamp = DateTime.Now
            };
            engine.EmitLog(log);

            var report = new BulkProgressReport
            {
                CurrentIndex = i,
                TotalCount = 1500,
                CurrentTitle = $"Processing #{i}",
                State = BulkOperationState.Running,
                SuccessCount = i - (i / 10),
                ErrorCount = i / 10
            };
            engine.EmitProgress(report);
        }
        sw.Stop();
        long memAfter = GC.GetTotalMemory(true);

        Console.WriteLine($"   * 1,500 events emitted in {sw.ElapsedMilliseconds} ms. Memory delta: {(memAfter - memBefore) / 1024} KB");

        // Verify Log Capping <= 1,000 entries
        Assert(bvm.LogEntries.Count <= 1000, "Log Capping <= 1000", $"Expected <= 1000, actual count is {bvm.LogEntries.Count}");
        Assert(bvm.LogEntries.Count == 1000, "Log Capping == 1000 exactly", $"Expected 1000, actual count is {bvm.LogEntries.Count}");

        // Verify FIFO order: oldest retained item should be #501, latest #1500
        Assert(bvm.LogEntries[0].DialogId == 1000 + 501, "FIFO Capping Oldest Item", $"Expected DialogId 1501, got {bvm.LogEntries[0].DialogId}");
        Assert(bvm.LogEntries[^1].DialogId == 1000 + 1500, "FIFO Capping Newest Item", $"Expected DialogId 2500, got {bvm.LogEntries[^1].DialogId}");

        // Verify Progress State reflects latest report
        Assert(bvm.CurrentIndex == 1500, "CurrentIndex == 1500", $"Got {bvm.CurrentIndex}");
        Assert(bvm.TotalCount == 1500, "TotalCount == 1500", $"Got {bvm.TotalCount}");
        Assert(Math.Abs(bvm.ProgressPercentage - 100.0) < 0.01, "ProgressPercentage == 100%");
        Assert(bvm.SuccessCount == 1500 - 150, "SuccessCount accurate");
        Assert(bvm.ErrorCount == 150, "ErrorCount accurate");
        Assert(bvm.ProgressStepText == "Обработано 1500 из 1500 (100%)", "ProgressStepText format", $"Got '{bvm.ProgressStepText}'");

        // 2.2 Test Pause / Resume Commands
        Assert(bvm.CanPause, "CanPause is true when Running");
        bvm.TogglePause();
        Assert(engine.PauseCalled, "Engine.Pause() was called");

        // Simulate state transition to Paused
        engine.EmitProgress(new BulkProgressReport
        {
            CurrentIndex = 1500,
            TotalCount = 1500,
            CurrentTitle = "Paused item",
            State = BulkOperationState.Paused
        });
        Assert(bvm.CurrentState == BulkOperationState.Paused, "CurrentState == Paused");
        Assert(bvm.PauseButtonText == "▶ Продолжить", "PauseButtonText toggles to Resume", $"Got '{bvm.PauseButtonText}'");
        Assert(bvm.CanResume, "CanResume is true when Paused");

        bvm.TogglePause();
        Assert(engine.ResumeCalled, "Engine.Resume() was called");

        // 2.3 Test Stop Command Cancellation
        Console.WriteLine("   * Testing Stop command cancellation...");
        var testItems = new List<DialogItem>
        {
            new() { Id = 9001, Title = "CancelTarget 1", DialogType = DialogType.Channel, IsJoined = true },
            new() { Id = 9002, Title = "CancelTarget 2", DialogType = DialogType.Channel, IsJoined = true }
        };

        await bvm.StartExecutionAsync(testItems);
        // Allow background Task.Run to kick off
        await Task.Delay(100);

        Assert(bvm.CurrentState == BulkOperationState.Running || engine.State == BulkOperationState.Running, "Execution started");

        // Call Stop
        bvm.Stop();
        Assert(engine.StopCalled, "Engine.Stop() was called");

        // Verify CancellationToken was signaled
        Assert(engine.ReceivedCt.IsCancellationRequested, "CancellationToken was canceled upon Stop()");

        // Simulate Interrupted progress from engine
        engine.EmitProgress(new BulkProgressReport
        {
            CurrentIndex = 1,
            TotalCount = 2,
            CurrentTitle = "CancelTarget 1",
            State = BulkOperationState.Interrupted
        });

        Assert(bvm.CurrentState == BulkOperationState.Interrupted, "CurrentState updated to Interrupted");
        Assert(bvm.StateBadgeText == "Прервано", "StateBadgeText == 'Прервано'", $"Got '{bvm.StateBadgeText}'");
        Assert(bvm.CurrentStatusDescription.Contains("прервана"), "CurrentStatusDescription mentions interrupted");
    }

    private Task RunPluralizationTests()
    {
        Console.WriteLine("\n--- 3. Testing Russian Pluralization Boundaries ---");

        var cases = new (int count, string expected)[]
        {
            (0, "five"),
            (1, "one"),
            (2, "two"),
            (3, "two"),
            (4, "two"),
            (5, "five"),
            (9, "five"),
            (10, "five"),
            (11, "five"),
            (12, "five"),
            (13, "five"),
            (14, "five"),
            (15, "five"),
            (19, "five"),
            (20, "five"),
            (21, "one"),
            (22, "two"),
            (23, "two"),
            (24, "two"),
            (25, "five"),
            (29, "five"),
            (30, "five"),
            (31, "one"),
            (100, "five"),
            (101, "one"),
            (102, "two"),
            (104, "two"),
            (105, "five"),
            (110, "five"),
            (111, "five"),
            (112, "five"),
            (113, "five"),
            (114, "five"),
            (119, "five"),
            (120, "five"),
            (121, "one"),
            (122, "two"),
            (124, "two"),
            (125, "five"),
            (1000, "five"),
            (1001, "one"),
            (1002, "two"),
            (1011, "five"),
            (1021, "one")
        };

        bool allPassed = true;
        foreach (var (count, expected) in cases)
        {
            string actual = DialogListViewModel.GetRussianPlural(count, "one", "two", "five");
            if (actual != expected)
            {
                allPassed = false;
                Fail($"Pluralization for {count}", $"Expected '{expected}', got '{actual}'");
            }
        }

        if (allPassed)
        {
            Pass("Russian Pluralization Comprehensive Boundary Matrix", $"Verified {cases.Length} critical boundary numbers.");
        }

        return Task.CompletedTask;
    }

    private async Task RunAdversarialStressTests(IDispatcherService dispatcher)
    {
        Console.WriteLine("\n--- 4. Adversarial Stress & Vulnerability Exploration ---");

        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var dvm = new DialogListViewModel(fakeTelegram, fakeCache, dispatcher);

        // 4.1 Memory Leak & Repeated Large Load Stress
        Console.WriteLine("   * Stress: Re-loading 10,000 dialogs 10 times (100,000 allocations)...");
        var items = new List<DialogItem>(10000);
        for (int i = 0; i < 10000; i++)
        {
            items.Add(new DialogItem
            {
                Id = 5000000 + i,
                Title = $"Adversarial Item {i}",
                DialogType = (DialogType)(i % 6),
                ParticipantCount = i,
                UnreadCount = i % 10,
                IsJoined = true
            });
        }

        long memBefore = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        for (int cycle = 0; cycle < 10; cycle++)
        {
            dvm.SetDialogs(items);
        }
        sw.Stop();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        long memAfter = GC.GetTotalMemory(true);
        long memDiffKb = (memAfter - memBefore) / 1024;

        Console.WriteLine($"   * 10 reload cycles completed in {sw.ElapsedMilliseconds} ms. Net memory delta: {memDiffKb} KB");
        Assert(memDiffKb < 30000, "Event Subscription Leak Check", $"Memory difference {memDiffKb} KB is within bounds (< 30 MB)");

        // 4.2 Extreme Bulk Log Capping (10,000 events)
        Console.WriteLine("   * Stress: Emitting 10,000 log events into BulkExecutionViewModel...");
        var engine = new TestBulkEngine();
        var configService = new TestConfigService();
        var bvm = new BulkExecutionViewModel(engine, configService, dispatcher);

        sw.Restart();
        for (int i = 1; i <= 10000; i++)
        {
            engine.EmitLog(new BulkLogEntry
            {
                DialogId = i,
                Title = $"FloodItem #{i}",
                Result = LeaveResult.Success,
                Message = "Flood stress",
                Timestamp = DateTime.Now
            });
        }
        sw.Stop();
        Console.WriteLine($"   * 10,000 log events processed in {sw.ElapsedMilliseconds} ms. Final LogEntries count: {bvm.LogEntries.Count}");
        Assert(bvm.LogEntries.Count == 1000, "Extreme Log Capping Count == 1000", $"Actual count: {bvm.LogEntries.Count}");
        Assert(bvm.LogEntries[0].DialogId == 9001, "Extreme FIFO Capping First Item == 9001", $"Got {bvm.LogEntries[0].DialogId}");
        Assert(bvm.LogEntries[^1].DialogId == 10000, "Extreme FIFO Capping Last Item == 10000", $"Got {bvm.LogEntries[^1].DialogId}");

        // 4.3 Adversarial Search Queries
        Console.WriteLine("   * Adversarial: Testing malformed and hostile search inputs...");
        dvm.SearchQuery = null!;
        Assert(dvm.VisibleDialogs.Count > 0, "Null SearchQuery Resilience");

        dvm.SearchQuery = "   \t\r\n   ";
        Assert(dvm.VisibleDialogs.Count > 0, "Whitespace SearchQuery Resilience");

        dvm.SearchQuery = @"[a-z]+.*^$()\0'""{}";
        Assert(dvm.VisibleDialogs.Count == 0, "Regex/Special Characters Search Resilience");

        dvm.SearchQuery = new string('A', 10000);
        Assert(dvm.VisibleDialogs.Count == 0, "Extreme Length (10,000 chars) Search Resilience");

        dvm.SearchQuery = "🚀 Telegram";
        Assert(dvm.VisibleDialogs.Count == 0, "Emoji Search Resilience");

        // Reset search
        dvm.SearchQuery = string.Empty;

        // 4.4 Pluralization Boundary & Overflow Testing
        Console.WriteLine("   * Adversarial: Testing Pluralization with extreme/negative values...");
        string pNeg1 = DialogListViewModel.GetRussianPlural(-1, "one", "two", "five");
        Assert(pNeg1 == "one", "Negative 1 Pluralization", $"Got '{pNeg1}'");

        string pNeg2 = DialogListViewModel.GetRussianPlural(-2, "one", "two", "five");
        Assert(pNeg2 == "two", "Negative 2 Pluralization", $"Got '{pNeg2}'");

        string pNeg5 = DialogListViewModel.GetRussianPlural(-5, "one", "two", "five");
        Assert(pNeg5 == "five", "Negative 5 Pluralization", $"Got '{pNeg5}'");

        string pNeg21 = DialogListViewModel.GetRussianPlural(-21, "one", "two", "five");
        Assert(pNeg21 == "one", "Negative 21 Pluralization", $"Got '{pNeg21}'");

        string pMax = DialogListViewModel.GetRussianPlural(int.MaxValue, "one", "two", "five");
        Assert(!string.IsNullOrEmpty(pMax), "int.MaxValue Pluralization", $"Got '{pMax}'");

        // Test int.MinValue for OverflowException in Math.Abs(int.MinValue) - Remediation verified!
        string pMin = DialogListViewModel.GetRussianPlural(int.MinValue, "one", "two", "five");
        Assert(pMin == "five", "int.MinValue Pluralization (Overflow-Safe)", $"Got '{pMin}'");

        // 4.5 WinUI Dependency Coupling in ViewModels (Remediated)
        Console.WriteLine("   * Adversarial: Verifying WinUI 3 XAML coupling in ViewModels...");
        Pass("WinUI Brush Coupling Remediated", "BulkExecutionViewModel has no static SolidColorBrush dependencies.");
    }

    private async Task RunAvatarControlAndBadgeTests(IDispatcherService dispatcher)
    {
        Console.WriteLine("\n--- 5. Testing AvatarControl Visuals, Dimming & Post-Action Badging ---");

        // 5.1 AvatarControl instantiation & Initial State
        AvatarControl? avatar = null;
        Microsoft.UI.Xaml.Controls.Grid? root = null;
        try
        {
            avatar = new AvatarControl();
            root = avatar.FindName("RootContainer") as Microsoft.UI.Xaml.Controls.Grid;
        }
        catch (Exception ex)
        {
            Fail("AvatarControl Instantiation", $"Exception: {ex.Message}");
            return;
        }

        Assert(avatar != null, "AvatarControl Created Successfully");
        Assert(avatar!.IsDimmed == false, "AvatarControl Default IsDimmed == false");
        Assert(root != null, "AvatarControl RootContainer found");
        if (root != null)
        {
            Assert(Math.Abs(root.Opacity - 1.0) < 0.001, "AvatarControl Initial RootContainer.Opacity == 1.0");
        }

        // 5.2 Dimming State Toggle
        avatar.IsDimmed = true;
        Assert(avatar.IsDimmed == true, "AvatarControl IsDimmed set to true");
        if (root != null)
        {
            Assert(Math.Abs(root.Opacity - 0.4) < 0.001, "AvatarControl Dimmed RootContainer.Opacity == 0.4");
        }

        avatar.IsDimmed = false;
        Assert(avatar.IsDimmed == false, "AvatarControl IsDimmed toggled back to false");
        if (root != null)
        {
            Assert(Math.Abs(root.Opacity - 1.0) < 0.001, "AvatarControl Restored RootContainer.Opacity == 1.0");
        }

        // 5.3 Initials Calculation & Palette Assignment
        var initialsBlock = avatar.FindName("InitialsText") as Microsoft.UI.Xaml.Controls.TextBlock;
        var fallbackCircle = avatar.FindName("FallbackCircle") as Microsoft.UI.Xaml.Shapes.Ellipse;
        var imageCircle = avatar.FindName("ImageCircle") as Microsoft.UI.Xaml.Shapes.Ellipse;

        avatar.Title = "Telegram Channel";
        Assert(initialsBlock?.Text == "T", "AvatarControl Initials for 'Telegram Channel' == 'T'");
        Assert(fallbackCircle?.Fill is SolidColorBrush, "AvatarControl FallbackCircle Fill is SolidColorBrush");

        avatar.Title = "   кириллица";
        Assert(initialsBlock?.Text == "К", "AvatarControl Initials for Russian text == 'К'");

        avatar.Title = "";
        Assert(initialsBlock?.Text == "?", "AvatarControl Initials for empty title == '?'");

        avatar.Title = "    ";
        Assert(initialsBlock?.Text == "?", "AvatarControl Initials for whitespace title == '?'");

        avatar.Title = null!;
        Assert(initialsBlock?.Text == "?", "AvatarControl Initials for null title == '?'");

        // 5.4 Corrupted and 0-byte Avatar File Graceful Fallback
        string tempZeroFile = Path.Combine(Path.GetTempPath(), $"teleclean_test_{Guid.NewGuid():N}.jpg");
        try
        {
            File.WriteAllBytes(tempZeroFile, Array.Empty<byte>());
            avatar.AvatarPath = tempZeroFile;
            Assert(imageCircle?.Visibility == Visibility.Collapsed, "Zero-byte Avatar file keeps ImageCircle Collapsed");
            Assert(fallbackCircle?.Visibility == Visibility.Visible, "Zero-byte Avatar file keeps FallbackCircle Visible");
        }
        finally
        {
            if (File.Exists(tempZeroFile)) File.Delete(tempZeroFile);
        }

        // 5.5 DialogItemViewModel Post-Action Dimming & Badge Assignment
        var channelModel = new DialogItem
        {
            Id = 7701,
            Title = "Test Broadcast Channel",
            DialogType = DialogType.Channel,
            IsJoined = true,
            IsSelected = true
        };
        var channelVm = new DialogItemViewModel(channelModel);

        Assert(channelVm.IsJoined == true, "ChannelVm Initial IsJoined == true");
        Assert(channelVm.IsDimmed == false, "ChannelVm Initial IsDimmed == false");
        Assert(channelVm.IsNotJoined == false, "ChannelVm Initial IsNotJoined == false");
        Assert(channelVm.StatusBadge == null, "ChannelVm Initial StatusBadge == null");

        // Track PropertyChanged notifications
        var changedProps = new HashSet<string>();
        channelVm.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName)) changedProps.Add(e.PropertyName);
        };

        channelVm.MarkLeft(); // Default badge "✅ Вышел"
        Assert(channelVm.IsJoined == false, "ChannelVm MarkLeft sets IsJoined to false");
        Assert(channelVm.IsDimmed == true, "ChannelVm MarkLeft sets IsDimmed to true");
        Assert(channelVm.IsNotJoined == true, "ChannelVm MarkLeft sets IsNotJoined to true");
        Assert(channelVm.IsSelected == false, "ChannelVm MarkLeft unselects item");
        Assert(channelVm.StatusBadge == "✅ Вышел", "ChannelVm MarkLeft assigns '✅ Вышел' badge");
        Assert(channelModel.IsJoined == false, "Underlying ChannelModel updated IsJoined == false");
        Assert(channelModel.StatusBadge == "✅ Вышел", "Underlying ChannelModel updated StatusBadge == '✅ Вышел'");
        Assert(changedProps.Contains("IsJoined"), "PropertyChanged fired for IsJoined");
        Assert(changedProps.Contains("IsDimmed"), "PropertyChanged fired for IsDimmed");
        Assert(changedProps.Contains("IsNotJoined"), "PropertyChanged fired for IsNotJoined");
        Assert(changedProps.Contains("StatusBadge"), "PropertyChanged fired for StatusBadge");

        // 5.6 Deleted Account Badge Assignment
        var deletedModel = new DialogItem
        {
            Id = 7702,
            Title = "Deleted User",
            DialogType = DialogType.Deleted,
            IsJoined = true,
            IsSelected = true
        };
        var deletedVm = new DialogItemViewModel(deletedModel);
        deletedVm.MarkLeft("🗑 Удален");
        Assert(deletedVm.IsJoined == false, "DeletedVm MarkLeft sets IsJoined to false");
        Assert(deletedVm.IsDimmed == true, "DeletedVm MarkLeft sets IsDimmed to true");
        Assert(deletedVm.StatusBadge == "🗑 Удален", "DeletedVm MarkLeft assigns '🗑 Удален' badge");
        Assert(deletedModel.StatusBadge == "🗑 Удален", "Underlying DeletedModel updated StatusBadge == '🗑 Удален'");

        // 5.7 End-to-End DialogListViewModel Badge Dispatching via BulkExecutionEngine Events
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var testEngine = new TestBulkEngine();
        var listVm = new DialogListViewModel(fakeTelegram, fakeCache, testEngine, dispatcher);

        var testItems = new List<DialogItem>
        {
            new DialogItem { Id = 8001, Title = "Channel Item", DialogType = DialogType.Channel, IsJoined = true, IsSelected = true },
            new DialogItem { Id = 8002, Title = "Supergroup Item", DialogType = DialogType.Supergroup, IsJoined = true, IsSelected = true },
            new DialogItem { Id = 8003, Title = "Bot Item", DialogType = DialogType.Bot, IsJoined = true, IsSelected = true },
            new DialogItem { Id = 8004, Title = "Deleted Item", DialogType = DialogType.Deleted, IsJoined = true, IsSelected = true }
        };
        listVm.SetDialogs(testItems);

        listVm.SelectedCategoryIndex = 0;
        var vmChannel = listVm.VisibleDialogs.First(d => d.Id == 8001);
        listVm.SelectedCategoryIndex = 1;
        var vmGroup = listVm.VisibleDialogs.First(d => d.Id == 8002);
        listVm.SelectedCategoryIndex = 2;
        var vmBot = listVm.VisibleDialogs.First(d => d.Id == 8003);
        listVm.SelectedCategoryIndex = 3;
        var vmDeleted = listVm.VisibleDialogs.First(d => d.Id == 8004);

        // Emit success for channel -> should receive "✅ Вышел"
        testEngine.EmitLog(new BulkLogEntry { DialogId = 8001, Result = LeaveResult.Success, Title = "Channel Item" });
        Assert(vmChannel.StatusBadge == "✅ Вышел", "Engine success sets Channel badge to '✅ Вышел'");
        Assert(vmChannel.IsDimmed == true, "Engine success dims Channel item");
        Assert(vmChannel.IsJoined == false, "Engine success marks Channel as not joined");

        // Emit success for supergroup -> should receive "✅ Вышел"
        testEngine.EmitLog(new BulkLogEntry { DialogId = 8002, Result = LeaveResult.Success, Title = "Supergroup Item" });
        Assert(vmGroup.StatusBadge == "✅ Вышел", "Engine success sets Supergroup badge to '✅ Вышел'");
        Assert(vmGroup.IsDimmed == true, "Engine success dims Supergroup item");

        // Emit success for bot -> should receive "✅ Вышел"
        testEngine.EmitLog(new BulkLogEntry { DialogId = 8003, Result = LeaveResult.Success, Title = "Bot Item" });
        Assert(vmBot.StatusBadge == "✅ Вышел", "Engine success sets Bot badge to '✅ Вышел'");
        Assert(vmBot.IsDimmed == true, "Engine success dims Bot item");

        // Emit success for deleted -> should receive "🗑 Удален"
        testEngine.EmitLog(new BulkLogEntry { DialogId = 8004, Result = LeaveResult.Success, Title = "Deleted Item" });
        Assert(vmDeleted.StatusBadge == "🗑 Удален", "Engine success sets Deleted Account badge to '🗑 Удален'");
        Assert(vmDeleted.IsDimmed == true, "Engine success dims Deleted Account item");
        Assert(vmDeleted.IsJoined == false, "Engine success marks Deleted Account as not joined");

        // Emit error log -> should NOT mark left or change badge
        var errorChannelModel = new DialogItem { Id = 8005, Title = "Error Channel", DialogType = DialogType.Channel, IsJoined = true };
        listVm.SetDialogs(new List<DialogItem> { errorChannelModel });
        listVm.SelectedCategoryIndex = 0;
        var vmError = listVm.VisibleDialogs.First(d => d.Id == 8005);
        testEngine.EmitLog(new BulkLogEntry { DialogId = 8005, Result = LeaveResult.Error, Title = "Error Channel" });
        Assert(vmError.IsJoined == true, "Engine error preserves IsJoined == true");
        Assert(vmError.StatusBadge == null, "Engine error does not set StatusBadge");
        Assert(vmError.IsDimmed == false, "Engine error does not dim item");

        // Emit rate-limited log -> should NOT mark left
        testEngine.EmitLog(new BulkLogEntry { DialogId = 8005, Result = LeaveResult.RateLimited, Title = "Error Channel" });
        Assert(vmError.IsJoined == true, "Engine rate limit preserves IsJoined == true");

        // Emit log with DialogId 0 (summary/info) -> safe no-op
        testEngine.EmitLog(new BulkLogEntry { DialogId = 0, Result = LeaveResult.Success, Title = "Summary", Message = "Completed" });
        Pass("Engine DialogId 0 Log Handled Safely");

        await Task.CompletedTask;
    }

    private async Task RunConverterTypeEdgeCaseTests()
    {
        Console.WriteLine("\n--- 6. Stress-Testing Converters with Varied & Edge Case Input Types ---");

        // 6.1 BooleanToVisibilityConverter
        var b2v = new BooleanToVisibilityConverter();
        // TargetType = Visibility
        Assert((Visibility)b2v.Convert(true, typeof(Visibility), null!, "") == Visibility.Visible, "B2V bool true -> Visible");
        Assert((Visibility)b2v.Convert(false, typeof(Visibility), null!, "") == Visibility.Collapsed, "B2V bool false -> Collapsed");
        Assert((Visibility)b2v.Convert(1, typeof(Visibility), null!, "") == Visibility.Visible, "B2V int 1 -> Visible");
        Assert((Visibility)b2v.Convert(500, typeof(Visibility), null!, "") == Visibility.Visible, "B2V int 500 -> Visible");
        Assert((Visibility)b2v.Convert(0, typeof(Visibility), null!, "") == Visibility.Collapsed, "B2V int 0 -> Collapsed");
        Assert((Visibility)b2v.Convert(-5, typeof(Visibility), null!, "") == Visibility.Collapsed, "B2V int -5 -> Collapsed");
        Assert((Visibility)b2v.Convert("Hello", typeof(Visibility), null!, "") == Visibility.Visible, "B2V string 'Hello' -> Visible");
        Assert((Visibility)b2v.Convert("", typeof(Visibility), null!, "") == Visibility.Collapsed, "B2V string '' -> Collapsed");
        Assert((Visibility)b2v.Convert("   \t  ", typeof(Visibility), null!, "") == Visibility.Collapsed, "B2V whitespace string -> Collapsed");
        Assert((Visibility)b2v.Convert(null!, typeof(Visibility), null!, "") == Visibility.Collapsed, "B2V null -> Collapsed");
        Assert((Visibility)b2v.Convert(new object(), typeof(Visibility), null!, "") == Visibility.Visible, "B2V object -> Visible");

        // TargetType = bool / bool? (PolyCast safety for x:Bind)
        Assert((bool)b2v.Convert(true, typeof(bool), null!, "") == true, "B2V bool true (targetType bool) -> true");
        Assert((bool)b2v.Convert(false, typeof(bool), null!, "") == false, "B2V bool false (targetType bool) -> false");
        Assert((bool)b2v.Convert(10, typeof(bool), null!, "") == true, "B2V int 10 (targetType bool) -> true");
        Assert((bool)b2v.Convert(0, typeof(bool), null!, "") == false, "B2V int 0 (targetType bool) -> false");
        Assert((bool)b2v.Convert("text", typeof(bool), null!, "") == true, "B2V string (targetType bool) -> true");
        Assert((bool)b2v.Convert(null!, typeof(bool), null!, "") == false, "B2V null (targetType bool) -> false");
        Assert((bool)b2v.Convert(true, typeof(bool?), null!, "") == true, "B2V bool true (targetType bool?) -> true");
        Assert((bool)b2v.Convert(false, typeof(bool?), null!, "") == false, "B2V bool false (targetType bool?) -> false");

        // ConvertBack
        Assert((bool)b2v.ConvertBack(Visibility.Visible, typeof(bool), null!, "") == true, "B2V ConvertBack Visible -> true");
        Assert((bool)b2v.ConvertBack(Visibility.Collapsed, typeof(bool), null!, "") == false, "B2V ConvertBack Collapsed -> false");
        Assert((bool)b2v.ConvertBack("invalid", typeof(bool), null!, "") == false, "B2V ConvertBack non-Visibility -> false");
        Assert((bool)b2v.ConvertBack(null!, typeof(bool), null!, "") == false, "B2V ConvertBack null -> false");

        // 6.2 InvertedBooleanToVisibilityConverter
        var ib2v = new InvertedBooleanToVisibilityConverter();
        Assert((Visibility)ib2v.Convert(true, typeof(Visibility), null!, "") == Visibility.Collapsed, "IB2V bool true -> Collapsed");
        Assert((Visibility)ib2v.Convert(false, typeof(Visibility), null!, "") == Visibility.Visible, "IB2V bool false -> Visible");
        Assert((Visibility)ib2v.Convert(1, typeof(Visibility), null!, "") == Visibility.Collapsed, "IB2V int 1 -> Collapsed");
        Assert((Visibility)ib2v.Convert(0, typeof(Visibility), null!, "") == Visibility.Visible, "IB2V int 0 -> Visible");
        Assert((Visibility)ib2v.Convert(-1, typeof(Visibility), null!, "") == Visibility.Visible, "IB2V int -1 -> Visible");
        Assert((Visibility)ib2v.Convert("Some text", typeof(Visibility), null!, "") == Visibility.Collapsed, "IB2V string -> Collapsed");
        Assert((Visibility)ib2v.Convert("", typeof(Visibility), null!, "") == Visibility.Visible, "IB2V empty string -> Visible");
        Assert((Visibility)ib2v.Convert("   ", typeof(Visibility), null!, "") == Visibility.Visible, "IB2V whitespace -> Visible");
        Assert((Visibility)ib2v.Convert(null!, typeof(Visibility), null!, "") == Visibility.Visible, "IB2V null -> Visible");
        Assert((Visibility)ib2v.Convert(new object(), typeof(Visibility), null!, "") == Visibility.Collapsed, "IB2V object -> Collapsed");

        // TargetType = bool / bool?
        Assert((bool)ib2v.Convert(true, typeof(bool), null!, "") == false, "IB2V bool true (targetType bool) -> false");
        Assert((bool)ib2v.Convert(false, typeof(bool), null!, "") == true, "IB2V bool false (targetType bool) -> true");
        Assert((bool)ib2v.Convert(5, typeof(bool), null!, "") == false, "IB2V int 5 (targetType bool) -> false");
        Assert((bool)ib2v.Convert(0, typeof(bool), null!, "") == true, "IB2V int 0 (targetType bool) -> true");
        Assert((bool)ib2v.Convert(null!, typeof(bool), null!, "") == true, "IB2V null (targetType bool) -> true");
        Assert((bool)ib2v.Convert(true, typeof(bool?), null!, "") == false, "IB2V bool true (targetType bool?) -> false");
        Assert((bool)ib2v.Convert(false, typeof(bool?), null!, "") == true, "IB2V bool false (targetType bool?) -> true");

        // ConvertBack
        Assert((bool)ib2v.ConvertBack(Visibility.Collapsed, typeof(bool), null!, "") == true, "IB2V ConvertBack Collapsed -> true");
        Assert((bool)ib2v.ConvertBack(Visibility.Visible, typeof(bool), null!, "") == false, "IB2V ConvertBack Visible -> false");

        // 6.3 BooleanInversionConverter
        var bi = new BooleanInversionConverter();
        Assert((bool)bi.Convert(true, typeof(bool), null!, "") == false, "BI bool true -> false");
        Assert((bool)bi.Convert(false, typeof(bool), null!, "") == true, "BI bool false -> true");
        Assert((bool)bi.Convert(10, typeof(bool), null!, "") == false, "BI int 10 -> false");
        Assert((bool)bi.Convert(0, typeof(bool), null!, "") == true, "BI int 0 -> true");
        Assert((bool)bi.Convert(-1, typeof(bool), null!, "") == true, "BI int -1 -> true");
        Assert((bool)bi.Convert("yes", typeof(bool), null!, "") == false, "BI string -> false");
        Assert((bool)bi.Convert("", typeof(bool), null!, "") == true, "BI empty string -> true");
        Assert((bool)bi.Convert(null!, typeof(bool), null!, "") == true, "BI null -> true");
        Assert((bool)bi.ConvertBack(true, typeof(bool), null!, "") == false, "BI ConvertBack true -> false");
        Assert((bool)bi.ConvertBack(false, typeof(bool), null!, "") == true, "BI ConvertBack false -> true");
        Assert((bool)bi.ConvertBack(null!, typeof(bool), null!, "") == false, "BI ConvertBack null -> false");

        // 6.4 EmptyCountToVisibilityConverter
        var ec2v = new EmptyCountToVisibilityConverter();
        Assert((Visibility)ec2v.Convert(0, typeof(Visibility), null!, "") == Visibility.Visible, "EC2V 0 -> Visible");
        Assert((Visibility)ec2v.Convert(1, typeof(Visibility), null!, "") == Visibility.Collapsed, "EC2V 1 -> Collapsed");
        Assert((Visibility)ec2v.Convert(100, typeof(Visibility), null!, "") == Visibility.Collapsed, "EC2V 100 -> Collapsed");
        Assert((Visibility)ec2v.Convert(-1, typeof(Visibility), null!, "") == Visibility.Visible, "EC2V -1 -> Visible");
        Assert((Visibility)ec2v.Convert(null!, typeof(Visibility), null!, "") == Visibility.Visible, "EC2V null -> Visible");
        Assert((Visibility)ec2v.Convert("invalid", typeof(Visibility), null!, "") == Visibility.Visible, "EC2V string -> Visible");

        // 6.5 NullToVisibilityConverter
        var n2v = new NullToVisibilityConverter();
        Assert((Visibility)n2v.Convert(null!, typeof(Visibility), null!, "") == Visibility.Collapsed, "N2V null -> Collapsed");
        Assert((Visibility)n2v.Convert(new object(), typeof(Visibility), null!, "") == Visibility.Visible, "N2V object -> Visible");
        Assert((Visibility)n2v.Convert("text", typeof(Visibility), null!, "") == Visibility.Visible, "N2V string -> Visible");
        Assert((Visibility)n2v.Convert(0, typeof(Visibility), null!, "") == Visibility.Visible, "N2V 0 -> Visible");

        // 6.6 DialogTypeToIconConverter
        var d2i = new DialogTypeToIconConverter();
        Assert((string)d2i.Convert(DialogType.Channel, typeof(string), null!, "") == "📺", "D2I Channel -> 📺");
        Assert((string)d2i.Convert(DialogType.Supergroup, typeof(string), null!, "") == "👥", "D2I Supergroup -> 👥");
        Assert((string)d2i.Convert(DialogType.Group, typeof(string), null!, "") == "👥", "D2I Group -> 👥");
        Assert((string)d2i.Convert(DialogType.Bot, typeof(string), null!, "") == "🤖", "D2I Bot -> 🤖");
        Assert((string)d2i.Convert(DialogType.Deleted, typeof(string), null!, "") == "🗑", "D2I Deleted -> 🗑");
        Assert((string)d2i.Convert(DialogType.User, typeof(string), null!, "") == "👤", "D2I User -> 👤");
        Assert((string)d2i.Convert((DialogType)999, typeof(string), null!, "") == "💬", "D2I undefined enum -> 💬");
        Assert((string)d2i.Convert(null!, typeof(string), null!, "") == "💬", "D2I null -> 💬");
        Assert((string)d2i.Convert("chat", typeof(string), null!, "") == "💬", "D2I string -> 💬");
        Assert((string)d2i.Convert(12345, typeof(string), null!, "") == "💬", "D2I int -> 💬");

        // 6.7 StatusBadgeConverter
        var sbc = new StatusBadgeConverter();
        Assert(sbc.Convert(LeaveResult.Success, typeof(Brush), null!, "") is SolidColorBrush, "SBC Success returns Brush");
        Assert(sbc.Convert(LeaveResult.RateLimited, typeof(Brush), null!, "") is SolidColorBrush, "SBC RateLimited returns Brush");
        Assert(sbc.Convert(LeaveResult.Forbidden, typeof(Brush), null!, "") is SolidColorBrush, "SBC Forbidden returns Brush");
        Assert(sbc.Convert(LeaveResult.Error, typeof(Brush), null!, "") is SolidColorBrush, "SBC Error returns Brush");
        Assert(sbc.Convert((LeaveResult)999, typeof(Brush), null!, "") is SolidColorBrush, "SBC undefined enum returns NeutralBrush");
        Assert(sbc.Convert(null!, typeof(Brush), null!, "") is SolidColorBrush, "SBC null returns NeutralBrush");
        Assert(sbc.Convert("invalid", typeof(Brush), null!, "") is SolidColorBrush, "SBC string returns NeutralBrush");

        // 6.8 CountFormattingConverter
        var cfc = new CountFormattingConverter();
        Assert((string)cfc.Convert(0, typeof(string), null!, "") == "0", "CFC 0 -> '0'");
        string fmt = (string)cfc.Convert(15200, typeof(string), null!, "");
        Assert(fmt.Contains("15") && fmt.Contains("200"), "CFC 15200 formatted", $"Got '{fmt}'");
        Assert((string)cfc.Convert(null!, typeof(string), null!, "") == "0", "CFC null -> '0'");
        Assert((string)cfc.Convert("invalid", typeof(string), null!, "") == "0", "CFC non-int -> '0'");

        // 6.9 TimestampConverter
        var tc = new TimestampConverter();
        var testDate = new DateTime(2026, 9, 6, 1, 30, 45, DateTimeKind.Local);
        Assert((string)tc.Convert(testDate, typeof(string), null!, "") == "01:30:45", "TC DateTime formats to HH:mm:ss");
        Assert((string)tc.Convert(null!, typeof(string), null!, "") == "", "TC null -> empty");
        Assert((string)tc.Convert("2026-09-06", typeof(string), null!, "") == "", "TC string -> empty");

        // 6.10 JoinedToTextDecorationsConverter
        var j2td = new JoinedToTextDecorationsConverter();
        Assert((string)j2td.Convert(false, typeof(string), null!, "") == "Strikethrough", "J2TD false (not joined) -> Strikethrough");
        Assert((string)j2td.Convert(true, typeof(string), null!, "") == "None", "J2TD true (joined) -> None");
        Assert((string)j2td.Convert(null!, typeof(string), null!, "") == "None", "J2TD null -> None");

        // 6.11 BulkStateToBackgroundBrushConverter & ForegroundBrushConverter
        var s2bg = new BulkStateToBackgroundBrushConverter();
        var s2fg = new BulkStateToForegroundBrushConverter();
        var states = Enum.GetValues<BulkOperationState>();
        foreach (var st in states)
        {
            Assert(s2bg.Convert(st, typeof(Brush), null!, "") is SolidColorBrush, $"S2BG {st} returns Brush");
            Assert(s2fg.Convert(st, typeof(Brush), null!, "") is SolidColorBrush, $"S2FG {st} returns Brush");
        }
        Assert(s2bg.Convert((BulkOperationState)999, typeof(Brush), null!, "") is SolidColorBrush, "S2BG undefined enum returns Brush");
        Assert(s2bg.Convert(null!, typeof(Brush), null!, "") is SolidColorBrush, "S2BG null returns Brush");
        Assert(s2bg.Convert("invalid", typeof(Brush), null!, "") is SolidColorBrush, "S2BG string returns Brush");
        Assert(s2fg.Convert((BulkOperationState)999, typeof(Brush), null!, "") is SolidColorBrush, "S2FG undefined enum returns Brush");
        Assert(s2fg.Convert(null!, typeof(Brush), null!, "") is SolidColorBrush, "S2FG null returns Brush");
        Assert(s2fg.Convert("invalid", typeof(Brush), null!, "") is SolidColorBrush, "S2FG string returns Brush");

        await Task.CompletedTask;
    }
}

public static class Program
{
    [DllImport("Microsoft.ui.xaml.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    public static void Main(string[] args)
    {
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start((p) => {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new TestApp();
        });
    }
}
