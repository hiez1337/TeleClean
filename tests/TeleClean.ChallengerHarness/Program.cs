using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TeleClean.App.Services;
using TeleClean.App.ViewModels;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;
using TeleClean.Tests.Mocks;

namespace TeleClean.ChallengerHarness;

public class DirectDispatcherService : IDispatcherService
{
    public bool HasThreadAccess => true;

    public void Enqueue(Action action)
    {
        action();
    }

    public Task EnqueueAsync(Func<Task> function)
    {
        return function();
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

public class MockBulkEngine : IBulkExecutionEngine
{
    public BulkOperationState State { get; set; } = BulkOperationState.Idle;
    public event EventHandler<BulkProgressReport>? ProgressChanged;
    public event EventHandler<BulkLogEntry>? LogEntryAdded;

    public bool StopCalled { get; private set; }
    public bool PauseCalled { get; private set; }
    public bool ResumeCalled { get; private set; }
    public CancellationToken ReceivedCt { get; private set; }

    public Task StartAsync(IEnumerable<DialogItem> items, TimeSpan delayBetween, TimeSpan rateLimitDelay, CancellationToken ct = default)
    {
        ReceivedCt = ct;
        State = BulkOperationState.Running;
        return Task.CompletedTask;
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
    }

    public void FireLog(BulkLogEntry log) => LogEntryAdded?.Invoke(this, log);
    public void FireProgress(BulkProgressReport report) => ProgressChanged?.Invoke(this, report);
}

public static class Program
{
    private static int _passedCount;
    private static int _failedCount;
    private static readonly List<string> Failures = new();

    private static void Pass(string testName, string details = "")
    {
        _passedCount++;
        Console.WriteLine($"  [PASS] {testName} {(string.IsNullOrEmpty(details) ? "" : $"({details})")}");
    }

    private static void Fail(string testName, string reason)
    {
        _failedCount++;
        string msg = $"{testName}: {reason}";
        Failures.Add(msg);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [FAIL] {msg}");
        Console.ResetColor();
    }

    private static void Assert(bool condition, string testName, string message = "")
    {
        if (condition)
            Pass(testName, message);
        else
            Fail(testName, message);
    }

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=======================================================================");
        Console.WriteLine("  CHALLENGER 1: EMPIRICAL STRESS & ADVERSARIAL VERIFICATION HARNESS   ");
        Console.WriteLine("=======================================================================");

        // Verify thread model: pure headless console thread (not WinUI UI thread)
        Console.WriteLine($"Thread ApartmentState: {Thread.CurrentThread.GetApartmentState()}");
        Console.WriteLine($"IsThreadPoolThread: {Thread.CurrentThread.IsThreadPoolThread}");

        TestRussianPluralization();
        await TestBulkExecutionViewModelHeadlessAsync();
        await TestLogEntryAddedIntegrationWithDialogListViewModelAsync();

        Console.WriteLine("\n=======================================================================");
        Console.WriteLine($"FINAL RESULT: {_passedCount} PASSED, {_failedCount} FAILED.");
        if (_failedCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAILURE SUMMARY:");
            foreach (var f in Failures)
            {
                Console.WriteLine($"  - {f}");
            }
            Console.ResetColor();
        }
        Console.WriteLine("=======================================================================");

        return _failedCount == 0 ? 0 : 1;
    }

    private static void TestRussianPluralization()
    {
        Console.WriteLine("\n--- FOCUS 1: Russian Pluralization Boundary & Overflow Tests ---");

        // 1.1 int.MinValue verification: Math.Abs(int.MinValue) throws OverflowException unless handled with long cast
        try
        {
            string minResult = DialogListViewModel.GetRussianPlural(int.MinValue, "one", "two", "five");
            // 2147483648: 48 % 10 = 8 -> "five"
            Assert(minResult == "five", "GetRussianPlural(int.MinValue)", $"Got '{minResult}', expected 'five'");
        }
        catch (OverflowException ex)
        {
            Fail("GetRussianPlural(int.MinValue)", $"Threw OverflowException: {ex.Message}");
        }
        catch (Exception ex)
        {
            Fail("GetRussianPlural(int.MinValue)", $"Threw unexpected exception: {ex}");
        }

        // 1.2 int.MaxValue verification
        try
        {
            string maxResult = DialogListViewModel.GetRussianPlural(int.MaxValue, "one", "two", "five");
            // 2147483647: 47 % 10 = 7 -> "five"
            Assert(maxResult == "five", "GetRussianPlural(int.MaxValue)", $"Got '{maxResult}', expected 'five'");
        }
        catch (Exception ex)
        {
            Fail("GetRussianPlural(int.MaxValue)", $"Threw exception: {ex}");
        }

        // 1.3 Linguistic Matrix Test
        var testMatrix = new (int count, string expected)[]
        {
            (0, "five"),
            (1, "one"),
            (2, "two"),
            (3, "two"),
            (4, "two"),
            (5, "five"),
            (6, "five"),
            (7, "five"),
            (8, "five"),
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
            (40, "five"),
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
            (115, "five"),
            (119, "five"),
            (120, "five"),
            (121, "one"),
            (122, "two"),
            (125, "five"),
            (1000, "five"),
            (1001, "one"),
            (1002, "two"),
            (1005, "five"),
            (1011, "five"),
            (1012, "five"),
            (1014, "five"),
            (1021, "one"),
            (1025, "five"),
            // Negatives
            (-1, "one"),
            (-2, "two"),
            (-4, "two"),
            (-5, "five"),
            (-11, "five"),
            (-12, "five"),
            (-14, "five"),
            (-21, "one"),
            (-22, "two"),
            (-25, "five"),
            (-100, "five"),
            (-101, "one"),
            (-111, "five")
        };

        bool allCorrect = true;
        foreach (var (cnt, exp) in testMatrix)
        {
            string res = DialogListViewModel.GetRussianPlural(cnt, "one", "two", "five");
            if (res != exp)
            {
                allCorrect = false;
                Fail($"Pluralization for {cnt}", $"Expected '{exp}', got '{res}'");
            }
        }
        if (allCorrect)
        {
            Pass("Comprehensive Linguistic Pluralization Matrix", $"All {testMatrix.Length} test cases passed.");
        }
    }

    private static async Task TestBulkExecutionViewModelHeadlessAsync()
    {
        Console.WriteLine("\n--- FOCUS 2: Headless BulkExecutionViewModel & COM Decoupling ---");

        // 2.1 Reflection Scan: verify NO WinUI/COM Brush types exist as fields or properties
        var vmType = typeof(BulkExecutionViewModel);
        var fields = vmType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        var winuiFields = fields.Where(f => f.FieldType.FullName != null &&
            (f.FieldType.FullName.Contains("Microsoft.UI.Xaml") || f.FieldType.FullName.Contains("SolidColorBrush"))).ToList();
        Assert(winuiFields.Count == 0, "No WinUI Brush Fields", $"Found {winuiFields.Count} WinUI fields: {string.Join(", ", winuiFields.Select(f => f.Name))}");

        var properties = vmType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        var winuiProps = properties.Where(p => p.PropertyType.FullName != null &&
            (p.PropertyType.FullName.Contains("Microsoft.UI.Xaml") || p.PropertyType.FullName.Contains("SolidColorBrush"))).ToList();
        Assert(winuiProps.Count == 0, "No WinUI Brush Properties", $"Found {winuiProps.Count} WinUI properties: {string.Join(", ", winuiProps.Select(p => p.Name))}");

        // 2.2 Pure Headless Instantiation (MTA thread, no WinUI context)
        BulkExecutionViewModel? bvm = null;
        var engine = new MockBulkEngine();
        var config = new TestConfigService();
        var dispatcher = new DirectDispatcherService();

        try
        {
            bvm = new BulkExecutionViewModel(engine, config, dispatcher);
            Pass("Headless Instantiation", "Successfully instantiated BulkExecutionViewModel on MTA thread without COM exception");
        }
        catch (Exception ex)
        {
            Fail("Headless Instantiation", $"Failed with exception: {ex}");
            return;
        }

        // 2.3 Verify Initial State Properties
        Assert(bvm.CurrentState == BulkOperationState.Idle, "Initial CurrentState == Idle");
        Assert(bvm.StateBadgeText == "Ожидание", "Initial StateBadgeText == 'Ожидание'");
        Assert(!bvm.CanPause, "CanPause is false when Idle");
        Assert(!bvm.CanResume, "CanResume is false when Idle");
        Assert(!bvm.CanStop, "CanStop is false when Idle");

        // 2.4 Progress and State Notifications via background thread
        await Task.Run(() =>
        {
            engine.FireProgress(new BulkProgressReport
            {
                CurrentIndex = 10,
                TotalCount = 100,
                CurrentTitle = "Headless Test Item",
                State = BulkOperationState.Running,
                SuccessCount = 9,
                ErrorCount = 1,
                SecondsRemaining = 45
            });
        });

        Assert(bvm.CurrentIndex == 10, "CurrentIndex updated asynchronously", $"Got {bvm.CurrentIndex}");
        Assert(bvm.TotalCount == 100, "TotalCount updated asynchronously", $"Got {bvm.TotalCount}");
        Assert(bvm.CurrentState == BulkOperationState.Running, "CurrentState updated to Running");
        Assert(bvm.CanPause, "CanPause is true when Running");
        Assert(bvm.CanStop, "CanStop is true when Running");
        Assert(bvm.StateBadgeText == "Выполняется", "StateBadgeText == 'Выполняется'");

        // 2.5 Log Capping under rapid multi-threaded emission
        Console.WriteLine("   * Stress: Emitting 5,000 log entries from parallel thread pool...");
        var sw = Stopwatch.StartNew();
        await Task.Run(() =>
        {
            for (int i = 1; i <= 5000; i++)
            {
                engine.FireLog(new BulkLogEntry
                {
                    DialogId = i,
                    Title = $"Dialog {i}",
                    Result = LeaveResult.Success,
                    Message = $"Headless log #{i}",
                    Timestamp = DateTime.UtcNow
                });
            }
        });
        sw.Stop();
        Console.WriteLine($"   * 5,000 log entries processed in {sw.ElapsedMilliseconds} ms");

        Assert(bvm.LogEntries.Count == 1000, "Headless Log Capping == 1000", $"Actual: {bvm.LogEntries.Count}");
        Assert(bvm.LogEntries[0].DialogId == 4001, "Oldest retained log entry is #4001", $"Got {bvm.LogEntries[0].DialogId}");
        Assert(bvm.LogEntries[^1].DialogId == 5000, "Newest retained log entry is #5000", $"Got {bvm.LogEntries[^1].DialogId}");

        // 2.6 Pause / Resume / Stop in Headless Environment
        bvm.TogglePause();
        Assert(engine.PauseCalled, "TogglePause invoked engine.Pause()");

        engine.FireProgress(new BulkProgressReport { State = BulkOperationState.Paused });
        Assert(bvm.CurrentState == BulkOperationState.Paused, "State updated to Paused");
        Assert(bvm.CanResume, "CanResume is true when Paused");
        Assert(bvm.PauseButtonText == "▶ Продолжить", "PauseButtonText updated");

        bvm.TogglePause();
        Assert(engine.ResumeCalled, "TogglePause invoked engine.Resume()");

        bvm.Stop();
        Assert(engine.StopCalled, "Stop command invoked engine.Stop()");

        // 2.7 CopyLog Graceful Failure in Headless Environment
        try
        {
            bvm.CopyLog();
            Pass("CopyLog Headless Resilience", "CopyLog did not crash in headless context without active clipboard");
        }
        catch (Exception ex)
        {
            Fail("CopyLog Headless Resilience", $"Threw uncaught exception: {ex}");
        }

        // 2.8 Clean Disposal
        try
        {
            bvm.Dispose();
            Pass("Headless Disposal", "BulkExecutionViewModel disposed cleanly");
        }
        catch (Exception ex)
        {
            Fail("Headless Disposal", $"Dispose threw: {ex}");
        }
    }

    private static async Task TestLogEntryAddedIntegrationWithDialogListViewModelAsync()
    {
        Console.WriteLine("\n--- FOCUS 3: IBulkExecutionEngine.LogEntryAdded Integration with DialogListViewModel ---");

        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new MockBulkEngine();
        var dispatcher = new DirectDispatcherService();

        var dvm = new DialogListViewModel(fakeTelegram, fakeCache, engine, dispatcher);

        // Prepare test items across categories
        var items = new List<DialogItem>
        {
            new DialogItem { Id = 101, Title = "Channel Alpha", DialogType = DialogType.Channel, IsJoined = true, IsSelected = true },
            new DialogItem { Id = 102, Title = "Channel Beta", DialogType = DialogType.Channel, IsJoined = true, IsSelected = true },
            new DialogItem { Id = 201, Title = "Group Gamma", DialogType = DialogType.Supergroup, IsJoined = true, IsSelected = true },
            new DialogItem { Id = 301, Title = "Bot Delta", DialogType = DialogType.Bot, IsJoined = true, IsSelected = true },
            new DialogItem { Id = 401, Title = "Deleted Epsilon", DialogType = DialogType.Deleted, IsJoined = true, IsSelected = true },
        };

        dvm.SetDialogs(items);

        // Verify Tab 0 (Channels) initial state
        dvm.SelectedCategoryIndex = 0; // Channels
        Assert(dvm.VisibleDialogs.Count == 2, "Tab 0 Initial Visible Count == 2");
        Assert(dvm.SelectedCount == 2, "Tab 0 Initial SelectedCount == 2");
        Assert(dvm.LeftCount == 0, "Tab 0 Initial LeftCount == 0");
        Assert(dvm.SelectionCounterText == "Выбрано: 2 из 2", "Tab 0 Initial Counter Text", $"Got '{dvm.SelectionCounterText}'");
        Assert(dvm.ActionButtonText == "🚪 Выйти из 2 каналов", "Tab 0 Initial Action Button Text", $"Got '{dvm.ActionButtonText}'");

        var ch1 = dvm.VisibleDialogs.First(d => d.Id == 101);
        Assert(ch1.IsJoined, "Channel 101 initially IsJoined == true");
        Assert(!ch1.IsDimmed, "Channel 101 initially IsDimmed == false");
        Assert(!ch1.IsNotJoined, "Channel 101 initially IsNotJoined == false");
        Assert(string.IsNullOrEmpty(ch1.StatusBadge), "Channel 101 initially StatusBadge is empty");

        // 3.1 Emit LogEntryAdded for Channel 101 with LeaveResult.Success from background thread
        Console.WriteLine("   * Firing LogEntryAdded for Channel 101 (Success) from background thread...");
        await Task.Run(() =>
        {
            engine.FireLog(new BulkLogEntry
            {
                DialogId = 101,
                Title = "Channel Alpha",
                Result = LeaveResult.Success,
                Message = "Left channel successfully",
                Timestamp = DateTime.UtcNow
            });
        });

        // Verify Channel 101 state updated
        Assert(!ch1.IsJoined, "Channel 101 IsJoined updated to false");
        Assert(ch1.IsDimmed, "Channel 101 IsDimmed updated to true");
        Assert(ch1.IsNotJoined, "Channel 101 IsNotJoined updated to true");
        Assert(!ch1.IsSelected, "Channel 101 IsSelected automatically unchecked by MarkLeft()");
        Assert(ch1.StatusBadge == "✅ Вышел", "Channel 101 StatusBadge == '✅ Вышел'", $"Got '{ch1.StatusBadge}'");

        // Verify DialogListViewModel counters updated
        Assert(dvm.LeftCount == 1, "Tab 0 LeftCount updated to 1", $"Got {dvm.LeftCount}");
        Assert(dvm.SelectedCount == 1, "Tab 0 SelectedCount decreased to 1", $"Got {dvm.SelectedCount}");
        Assert(dvm.SelectionCounterText == "Выбрано: 1 из 2 | Вышло: 1", "Counter text displays 'Вышло: 1'", $"Got '{dvm.SelectionCounterText}'");
        Assert(dvm.ActionButtonText == "🚪 Выйти из 1 канала", "Action button text updated for 1 remaining channel", $"Got '{dvm.ActionButtonText}'");

        // 3.2 Emit LogEntryAdded for Deleted Epsilon (Id: 401) with LeaveResult.Success
        Console.WriteLine("   * Firing LogEntryAdded for Deleted Epsilon 401 (Success)...");
        await Task.Run(() =>
        {
            engine.FireLog(new BulkLogEntry
            {
                DialogId = 401,
                Title = "Deleted Epsilon",
                Result = LeaveResult.Success,
                Message = "Deleted dialog successfully",
                Timestamp = DateTime.UtcNow
            });
        });

        // Switch to Tab 3 (Deleted)
        dvm.SelectedCategoryIndex = 3;
        Assert(dvm.VisibleDialogs.Count == 1, "Tab 3 Visible Count == 1");
        var del1 = dvm.VisibleDialogs.First(d => d.Id == 401);

        Assert(!del1.IsJoined, "Deleted 401 IsJoined == false");
        Assert(del1.IsDimmed, "Deleted 401 IsDimmed == true");
        Assert(!del1.IsSelected, "Deleted 401 IsSelected == false");
        Assert(del1.StatusBadge == "🗑 Удален", "Deleted 401 StatusBadge == '🗑 Удален' (Specific badge for Deleted)", $"Got '{del1.StatusBadge}'");
        Assert(dvm.LeftCount == 1, "Tab 3 LeftCount == 1");
        Assert(dvm.SelectedCount == 0, "Tab 3 SelectedCount == 0");
        Assert(dvm.SelectionCounterText == "Выбрано: 0 из 1 | Вышло: 1", "Tab 3 Counter text", $"Got '{dvm.SelectionCounterText}'");
        Assert(dvm.ActionButtonText == "🗑 Удалить выбранные", "Tab 3 Action button text (0 selected)", $"Got '{dvm.ActionButtonText}'");

        // 3.3 Emit LogEntryAdded with LeaveResult.Error: should NOT mark left
        Console.WriteLine("   * Firing LogEntryAdded for Channel Beta 102 with LeaveResult.Error...");
        await Task.Run(() =>
        {
            engine.FireLog(new BulkLogEntry
            {
                DialogId = 102,
                Title = "Channel Beta",
                Result = LeaveResult.Error,
                Message = "RPC error simulated",
                Timestamp = DateTime.UtcNow
            });
        });

        dvm.SelectedCategoryIndex = 0; // Channels
        var ch2 = dvm.VisibleDialogs.First(d => d.Id == 102);
        Assert(ch2.IsJoined, "Channel 102 IsJoined remains true after Error");
        Assert(!ch2.IsDimmed, "Channel 102 IsDimmed remains false after Error");
        Assert(string.IsNullOrEmpty(ch2.StatusBadge), "Channel 102 StatusBadge remains empty after Error");

        // 3.4 Emit LogEntryAdded with unknown DialogId or 0: should not throw
        try
        {
            engine.FireLog(new BulkLogEntry
            {
                DialogId = 999999,
                Title = "Ghost Item",
                Result = LeaveResult.Success,
                Message = "Ghost",
                Timestamp = DateTime.UtcNow
            });
            engine.FireLog(new BulkLogEntry
            {
                DialogId = 0,
                Title = "General Message",
                Result = LeaveResult.Success,
                Message = "Summary message",
                Timestamp = DateTime.UtcNow
            });
            Pass("Unknown DialogId Log Resilience", "Successfully ignored log entry with unknown or 0 DialogId");
        }
        catch (Exception ex)
        {
            Fail("Unknown DialogId Log Resilience", $"Threw exception: {ex}");
        }

        // 3.5 Clean Disposal unhooks events
        dvm.Dispose();
        // Fire again: should not update anything because unsubscribed
        engine.FireLog(new BulkLogEntry
        {
            DialogId = 102,
            Title = "Channel Beta",
            Result = LeaveResult.Success,
            Message = "After dispose",
            Timestamp = DateTime.UtcNow
        });
        Assert(ch2.IsJoined, "Unsubscribed after Dispose: Channel 102 was not mutated");
    }
}
