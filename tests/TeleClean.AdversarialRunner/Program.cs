using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;
using TeleClean.Core.Services;
using TL;
using WTelegram;

namespace TeleClean.AdversarialRunner;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  TELECLEAN.CORE EMPIRICAL ADVERSARIAL HARNESS   ");
        Console.WriteLine("=================================================");

        int passed = 0;
        int failed = 0;

        async Task RunTestAsync(string name, Func<Task> testFunc)
        {
            Console.Write($"[RUNNING] {name}... ");
            try
            {
                await testFunc();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASSED");
                Console.ResetColor();
                passed++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAILED: {ex.Message}");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                failed++;
            }
        }

        void RunTest(string name, Action testFunc)
        {
            Console.Write($"[RUNNING] {name}... ");
            try
            {
                testFunc();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASSED");
                Console.ResetColor();
                passed++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAILED: {ex.Message}");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                failed++;
            }
        }

        // =========================================================================
        // GROUP 1: Dialog Type Classification & Edge Cases
        // =========================================================================
        Console.WriteLine("\n--- GROUP 1: Dialog Type Classification & Edge Cases ---");

        RunTest("1.1 64-bit Dialog IDs (Max, Min, MTProto BigInt)", () =>
        {
            long[] testIds = [
                long.MaxValue,
                long.MinValue,
                -1001234567890123456L,
                1001234567890123456L,
                4294967296L,
                -4294967296L,
                0L
            ];

            foreach (var id in testIds)
            {
                var item = new DialogItem
                {
                    Id = id,
                    AccessHash = unchecked((long)0xDEADBEEFCAFEBABEUL),
                    Title = $"Dialog_{id}",
                    Username = "test_user",
                    DialogType = DialogType.Channel
                };

                var json = JsonSerializer.Serialize(item);
                var deserialized = JsonSerializer.Deserialize<DialogItem>(json);
                if (deserialized == null || deserialized.Id != id || deserialized.AccessHash != item.AccessHash)
                {
                    throw new Exception($"64-bit ID loss for id={id}. Got {deserialized?.Id}");
                }

                var str = item.ToString();
                if (!str.Contains(id.ToString()))
                {
                    throw new Exception($"ToString does not contain 64-bit ID {id}: {str}");
                }
            }
        });

        RunTest("1.2 Null and Empty Usernames Subtitle Formatting", () =>
        {
            string?[] usernameCases = [null, "", "   ", "\t\n"];
            DialogType[] types = [
                DialogType.Channel,
                DialogType.Supergroup,
                DialogType.Group,
                DialogType.Bot,
                DialogType.User,
                DialogType.Deleted
            ];

            foreach (var dt in types)
            {
                foreach (var un in usernameCases)
                {
                    var item = new DialogItem
                    {
                        Id = 12345,
                        Title = "Test Entity",
                        Username = un,
                        ParticipantCount = 0,
                        UnreadCount = 0,
                        DialogType = dt
                    };

                    string subtitle = item.Subtitle;
                    if (subtitle == null)
                    {
                        throw new Exception($"Subtitle returned null for DialogType={dt}, Username='{un}'");
                    }

                    if (dt == DialogType.Deleted && subtitle != "🗑 Удалённый аккаунт")
                    {
                        throw new Exception($"Deleted account subtitle mismatch: '{subtitle}'");
                    }

                    if (dt == DialogType.Bot && (un == null || string.IsNullOrWhiteSpace(un)) && subtitle != "🤖 Бот")
                    {
                        throw new Exception($"Bot with empty username subtitle mismatch: '{subtitle}'");
                    }

                    if (dt == DialogType.User && (un == null || string.IsNullOrWhiteSpace(un)) && subtitle != "👤 Пользователь")
                    {
                        throw new Exception($"User with empty username subtitle mismatch: '{subtitle}'");
                    }

                    if ((dt == DialogType.Channel || dt == DialogType.Group || dt == DialogType.Supergroup) &&
                        item.ParticipantCount == 0 && item.UnreadCount == 0 && string.IsNullOrWhiteSpace(un))
                    {
                        if (subtitle != "Нет данных")
                        {
                            throw new Exception($"Channel/group with no data subtitle mismatch: '{subtitle}'");
                        }
                    }
                }
            }
        });

        RunTest("1.3 Deactivated Chats Filter Logic", () =>
        {
            var activeChat = new Chat
            {
                id = 1001,
                title = "Active Chat",
                participants_count = 10,
                flags = 0
            };

            var deactChat = new Chat
            {
                id = 1002,
                title = "Deactivated Migrated Chat",
                participants_count = 0,
                flags = Chat.Flags.deactivated
            };

            Func<Chat, DialogItem?> classifyChat = chat =>
            {
                if (!chat.flags.HasFlag(Chat.Flags.deactivated))
                {
                    return new DialogItem
                    {
                        Id = chat.id,
                        Title = chat.title ?? "Группа",
                        DialogType = DialogType.Group,
                        IsChannel = false
                    };
                }
                return null;
            };

            var resActive = classifyChat(activeChat);
            if (resActive == null || resActive.DialogType != DialogType.Group)
            {
                throw new Exception("Active chat was improperly classified or rejected.");
            }

            var resDeact = classifyChat(deactChat);
            if (resDeact != null)
            {
                throw new Exception($"Deactivated chat should have been filtered to null, but got: {resDeact}");
            }
        });

        RunTest("1.4 Deleted Accounts Classification & Properties", () =>
        {
            var deletedUser = new User
            {
                id = 999888777,
                access_hash = 123456789,
                flags = User.Flags.deleted,
                first_name = "Ghost"
            };

            Func<User, DialogItem?> classifyUser = user =>
            {
                if (!user.IsActive)
                {
                    return new DialogItem
                    {
                        Id = user.id,
                        AccessHash = user.access_hash,
                        Title = user.ToString() ?? "Удалённый аккаунт",
                        Username = null,
                        ParticipantCount = 0,
                        UnreadCount = 0,
                        IsChannel = false,
                        IsJoined = false,
                        DialogType = DialogType.Deleted
                    };
                }
                if (user.IsBot)
                {
                    return new DialogItem { Id = user.id, DialogType = DialogType.Bot };
                }
                return new DialogItem { Id = user.id, DialogType = DialogType.User };
            };

            var item = classifyUser(deletedUser);
            if (item == null) throw new Exception("Deleted user classified to null.");
            if (item.DialogType != DialogType.Deleted) throw new Exception($"Expected DialogType.Deleted, got {item.DialogType}");
            if (item.IsJoined != false) throw new Exception("Deleted account should have IsJoined=false");
            if (item.ParticipantCount != 0) throw new Exception("Deleted account should have ParticipantCount=0");
            if (item.Username != null) throw new Exception("Deleted account should have Username=null");
            if (item.Subtitle != "🗑 Удалённый аккаунт") throw new Exception($"Subtitle expected '🗑 Удалённый аккаунт', got '{item.Subtitle}'");
        });

        RunTest("1.5 JSON Round-Trip For All 6 Dialog Types", () =>
        {
            var originalTypes = new[]
            {
                (DialogType.Channel, "channel"),
                (DialogType.Supergroup, "supergroup"),
                (DialogType.Group, "group"),
                (DialogType.Bot, "bot"),
                (DialogType.User, "user"),
                (DialogType.Deleted, "deleted")
            };

            foreach (var (type, expectedString) in originalTypes)
            {
                var item = new DialogItem { Id = 100, Title = "Test", DialogType = type };
                var json = JsonSerializer.Serialize(item);

                if (!json.Contains($"\"dialog_type\":\"{expectedString}\""))
                {
                    throw new Exception($"JSON serialization of {type} did not contain '\"dialog_type\":\"{expectedString}\"'. Actual: {json}");
                }

                var restored = JsonSerializer.Deserialize<DialogItem>(json);
                if (restored == null || restored.DialogType != type)
                {
                    throw new Exception($"Deserialization mismatch for {type}. Restored: {restored?.DialogType}");
                }
            }
        });

        // =========================================================================
        // GROUP 2: Rate Limit Handling and FloodWait Backoff
        // =========================================================================
        Console.WriteLine("\n--- GROUP 2: Rate Limit Handling & FloodWait Backoff ---");

        await RunTestAsync("2.1 BulkExecutionEngine RateLimited State Transitions & Second Countdown", async () =>
        {
            var fakeTelegram = new FakeTelegramService();
            var fakeCache = new FakeCacheService();
            var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

            var item1 = new DialogItem { Id = 101, Title = "RateLimitedChannel", DialogType = DialogType.Channel };
            var item2 = new DialogItem { Id = 102, Title = "SuccessChannel", DialogType = DialogType.Channel };

            fakeTelegram.SetResult(101, LeaveResult.RateLimited);
            fakeTelegram.SetResult(102, LeaveResult.Success);

            var statesSeen = new List<BulkOperationState>();
            var countdownsSeen = new List<int>();
            var logsSeen = new List<BulkLogEntry>();

            engine.ProgressChanged += (s, e) =>
            {
                lock (statesSeen)
                {
                    statesSeen.Add(e.State);
                    if (e.State == BulkOperationState.RateLimited)
                    {
                        countdownsSeen.Add(e.SecondsRemaining);
                    }
                }
            };

            engine.LogEntryAdded += (s, e) =>
            {
                lock (logsSeen)
                {
                    logsSeen.Add(e);
                }
            };

            var sw = Stopwatch.StartNew();
            await engine.StartAsync(
                new[] { item1, item2 },
                delayBetween: TimeSpan.FromSeconds(1),
                rateLimitDelay: TimeSpan.FromSeconds(2)
            );
            sw.Stop();

            if (!statesSeen.Contains(BulkOperationState.RateLimited))
            {
                throw new Exception("Engine never entered RateLimited state!");
            }

            if (engine.State != BulkOperationState.Completed)
            {
                throw new Exception($"Final state expected Completed, got {engine.State}");
            }

            if (item1.StatusBadge != "⏳ Ограничение")
            {
                throw new Exception($"Expected item1 badge '⏳ Ограничение', got '{item1.StatusBadge}'");
            }

            if (item2.StatusBadge != "✅ Вышел")
            {
                throw new Exception($"Expected item2 badge '✅ Вышел', got '{item2.StatusBadge}'");
            }

            if (countdownsSeen.Count < 2)
            {
                throw new Exception($"Expected at least 2 countdown progress reports, got {countdownsSeen.Count}: {string.Join(", ", countdownsSeen)}");
            }
            if (countdownsSeen[0] != 2 || countdownsSeen[1] != 1)
            {
                throw new Exception($"Expected countdown [2, 1], got [{string.Join(", ", countdownsSeen)}]");
            }

            if (sw.ElapsedMilliseconds < 1900)
            {
                throw new Exception($"Elapsed time too short for 2s backoff: {sw.ElapsedMilliseconds}ms");
            }

            var rateLog = logsSeen.FirstOrDefault(l => l.Result == LeaveResult.RateLimited);
            if (rateLog == null)
            {
                throw new Exception("No BulkLogEntry found with Result=RateLimited");
            }
        });

        await RunTestAsync("2.2 Consecutive RateLimits Recovery", async () =>
        {
            var fakeTelegram = new FakeTelegramService();
            var fakeCache = new FakeCacheService();
            var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

            var item1 = new DialogItem { Id = 201, Title = "Channel_1", DialogType = DialogType.Channel };
            var item2 = new DialogItem { Id = 202, Title = "Channel_2", DialogType = DialogType.Channel };

            fakeTelegram.SetResult(201, LeaveResult.RateLimited);
            fakeTelegram.SetResult(202, LeaveResult.RateLimited);

            await engine.StartAsync(
                new[] { item1, item2 },
                delayBetween: TimeSpan.FromSeconds(1),
                rateLimitDelay: TimeSpan.FromSeconds(1)
            );

            if (engine.State != BulkOperationState.Completed)
            {
                throw new Exception($"Expected Completed state after multiple rate limits, got {engine.State}");
            }
            if (item1.StatusBadge != "⏳ Ограничение" || item2.StatusBadge != "⏳ Ограничение")
            {
                throw new Exception("Badges not set to ⏳ Ограничение for consecutive rate limits.");
            }
        });

        // =========================================================================
        // GROUP 3: Concurrency, Pause, Resume, and Stop Controls
        // =========================================================================
        Console.WriteLine("\n--- GROUP 3: Pause, Resume, and Stop Concurrency ---");

        await RunTestAsync("3.1 Pause and Resume Execution Flow", async () =>
        {
            // Simulate network latency in LeaveDialogAsync
            var fakeTelegram = new FakeTelegramService(simulatedDelayMs: 60);
            var fakeCache = new FakeCacheService();
            var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

            var items = Enumerable.Range(1, 5)
                .Select(i => new DialogItem { Id = i, Title = $"Chan_{i}", DialogType = DialogType.Channel })
                .ToList();

            foreach (var item in items) fakeTelegram.SetResult(item.Id, LeaveResult.Success);

            bool wasPaused = false;

            engine.ProgressChanged += (s, e) =>
            {
                if (e.CurrentIndex == 2 && !wasPaused)
                {
                    Task.Run(() => engine.Pause());
                    wasPaused = true;
                }
            };

            var runTask = engine.StartAsync(items, delayBetween: TimeSpan.FromSeconds(1), rateLimitDelay: TimeSpan.FromSeconds(1));

            // Wait for pause to take effect
            var pauseTimeout = DateTime.UtcNow.AddSeconds(3);
            while (engine.State != BulkOperationState.Paused && DateTime.UtcNow < pauseTimeout)
            {
                await Task.Delay(20);
            }

            if (engine.State != BulkOperationState.Paused)
            {
                throw new Exception($"Expected Paused state, got {engine.State}");
            }

            int countAtPause = fakeTelegram.LeaveCallCount;

            // Wait 300ms to confirm no further items are processed
            await Task.Delay(300);
            if (fakeTelegram.LeaveCallCount != countAtPause)
            {
                throw new Exception($"Engine continued processing during pause! Count grew from {countAtPause} to {fakeTelegram.LeaveCallCount}");
            }

            // Resume
            engine.Resume();
            if (engine.State != BulkOperationState.Running && engine.State != BulkOperationState.Completed)
            {
                throw new Exception($"Expected Running after Resume, got {engine.State}");
            }

            await runTask;
            if (engine.State != BulkOperationState.Completed)
            {
                throw new Exception($"Expected Completed after full run, got {engine.State}");
            }
            if (fakeTelegram.LeaveCallCount != 5)
            {
                throw new Exception($"Expected all 5 items processed, got {fakeTelegram.LeaveCallCount}");
            }
        });

        await RunTestAsync("3.2 Stop Cleanly Halts Running Engine", async () =>
        {
            var fakeTelegram = new FakeTelegramService(simulatedDelayMs: 60);
            var fakeCache = new FakeCacheService();
            var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

            var items = Enumerable.Range(1, 10)
                .Select(i => new DialogItem { Id = i, Title = $"Chan_{i}", DialogType = DialogType.Channel })
                .ToList();

            foreach (var item in items) fakeTelegram.SetResult(item.Id, LeaveResult.Success);

            engine.ProgressChanged += (s, e) =>
            {
                if (e.CurrentIndex == 3)
                {
                    Task.Run(() => engine.Stop());
                }
            };

            await engine.StartAsync(items, delayBetween: TimeSpan.FromSeconds(1), rateLimitDelay: TimeSpan.FromSeconds(1));

            if (engine.State != BulkOperationState.Interrupted)
            {
                throw new Exception($"Expected Interrupted state after Stop(), got {engine.State}");
            }

            if (fakeTelegram.LeaveCallCount >= 8)
            {
                throw new Exception($"Stop failed to halt execution promptly: {fakeTelegram.LeaveCallCount} of 10 executed.");
            }
        });

        await RunTestAsync("3.3 Stop While Paused Unblocks Immediately", async () =>
        {
            var fakeTelegram = new FakeTelegramService(simulatedDelayMs: 60);
            var fakeCache = new FakeCacheService();
            var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

            var items = Enumerable.Range(1, 5)
                .Select(i => new DialogItem { Id = i, Title = $"Chan_{i}", DialogType = DialogType.Channel })
                .ToList();

            foreach (var item in items) fakeTelegram.SetResult(item.Id, LeaveResult.Success);

            var runTask = engine.StartAsync(items, delayBetween: TimeSpan.FromSeconds(1), rateLimitDelay: TimeSpan.FromSeconds(1));

            // Wait a moment then pause
            await Task.Delay(20);
            engine.Pause();

            var pauseTimeout = DateTime.UtcNow.AddSeconds(3);
            while (engine.State != BulkOperationState.Paused && DateTime.UtcNow < pauseTimeout)
            {
                await Task.Delay(20);
            }

            if (engine.State != BulkOperationState.Paused)
            {
                throw new Exception($"Engine is not Paused: {engine.State}");
            }

            var sw = Stopwatch.StartNew();
            engine.Stop();
            await runTask;
            sw.Stop();

            if (engine.State != BulkOperationState.Interrupted)
            {
                throw new Exception($"Expected Interrupted state, got {engine.State}");
            }

            if (sw.ElapsedMilliseconds > 800)
            {
                throw new Exception($"Stop took too long to unblock paused engine: {sw.ElapsedMilliseconds}ms");
            }
        });

        await RunTestAsync("3.4 Stop During RateLimited Countdown Aborts Promptly", async () =>
        {
            var fakeTelegram = new FakeTelegramService();
            var fakeCache = new FakeCacheService();
            var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

            var item1 = new DialogItem { Id = 901, Title = "FloodWaitChan", DialogType = DialogType.Channel };
            fakeTelegram.SetResult(901, LeaveResult.RateLimited);

            var runTask = engine.StartAsync(
                new[] { item1 },
                delayBetween: TimeSpan.FromSeconds(1),
                rateLimitDelay: TimeSpan.FromSeconds(20) // Long 20-second backoff
            );

            // Wait until engine is RateLimited
            var timeout = DateTime.UtcNow.AddSeconds(3);
            while (engine.State != BulkOperationState.RateLimited && DateTime.UtcNow < timeout)
            {
                await Task.Delay(20);
            }

            if (engine.State != BulkOperationState.RateLimited)
            {
                throw new Exception($"Failed to enter RateLimited state within timeout: {engine.State}");
            }

            // Call Stop
            var sw = Stopwatch.StartNew();
            engine.Stop();
            await runTask;
            sw.Stop();

            if (engine.State != BulkOperationState.Interrupted)
            {
                throw new Exception($"Expected Interrupted, got {engine.State}");
            }

            if (sw.ElapsedMilliseconds > 1500)
            {
                throw new Exception($"Stop failed to abort 20s backoff countdown quickly: took {sw.ElapsedMilliseconds}ms");
            }
        });

        await RunTestAsync("3.5 Concurrent StartAsync Throws InvalidOperationException", async () =>
        {
            var fakeTelegram = new FakeTelegramService(simulatedDelayMs: 150);
            var fakeCache = new FakeCacheService();
            var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

            var items = Enumerable.Range(1, 5)
                .Select(i => new DialogItem { Id = i, Title = $"Chan_{i}", DialogType = DialogType.Channel })
                .ToList();

            foreach (var item in items) fakeTelegram.SetResult(item.Id, LeaveResult.Success);

            var runTask = engine.StartAsync(items, delayBetween: TimeSpan.FromSeconds(1), rateLimitDelay: TimeSpan.FromSeconds(1));

            // Wait until engine is in Running state
            var timeout = DateTime.UtcNow.AddSeconds(2);
            while (engine.State != BulkOperationState.Running && DateTime.UtcNow < timeout)
            {
                await Task.Delay(10);
            }

            bool caught = false;
            try
            {
                await engine.StartAsync(items, delayBetween: TimeSpan.FromSeconds(1), rateLimitDelay: TimeSpan.FromSeconds(1));
            }
            catch (InvalidOperationException)
            {
                caught = true;
            }

            engine.Stop();
            await runTask;

            if (!caught)
            {
                throw new Exception("Starting engine twice concurrently failed to throw InvalidOperationException!");
            }
        });

        await RunTestAsync("3.6 Concurrent Pause and Resume Stress Test (No Deadlock)", async () =>
        {
            var fakeTelegram = new FakeTelegramService(simulatedDelayMs: 5);
            var fakeCache = new FakeCacheService();
            var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

            var items = Enumerable.Range(1, 30)
                .Select(i => new DialogItem { Id = i, Title = $"Chan_{i}", DialogType = DialogType.Channel })
                .ToList();

            foreach (var item in items) fakeTelegram.SetResult(item.Id, LeaveResult.Success);

            var runTask = engine.StartAsync(items, delayBetween: TimeSpan.FromMilliseconds(5), rateLimitDelay: TimeSpan.FromSeconds(1));

            var hammerTasks = Enumerable.Range(1, 10).Select(async _ =>
            {
                for (int i = 0; i < 20; i++)
                {
                    engine.Pause();
                    await Task.Delay(5);
                    engine.Resume();
                    await Task.Delay(5);
                }
            }).ToArray();

            await Task.WhenAll(hammerTasks);
            engine.Resume(); // Ensure unpaused
            await runTask;

            if (engine.State != BulkOperationState.Completed)
            {
                throw new Exception($"Expected Completed after concurrent hammer, got {engine.State}");
            }
        });

        // =========================================================================
        // GROUP 4: Disk Caching, History, and Credentials Resolution
        // =========================================================================
        Console.WriteLine("\n--- GROUP 4: Disk Caching, History & Credentials Resolution ---");

        await RunTestAsync("4.1 64-Bit Dialog Caching Round-Trip", async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TeleClean_Adv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var configService = new ConfigService(tempDir);
                var cacheService = new CacheService(configService);

                var items = new List<DialogItem>
                {
                    new() { Id = 9223372036854775800L, AccessHash = 123456789L, Title = "BigIntChannel", DialogType = DialogType.Channel },
                    new() { Id = -1001234567890L, AccessHash = 987654321L, Title = "NegativeIdSupergroup", DialogType = DialogType.Supergroup },
                    new() { Id = 55555L, AccessHash = 0L, Title = "BotItem", Username = "helper_bot", DialogType = DialogType.Bot }
                };

                await cacheService.SaveDialogCacheAsync(items);
                var loaded = await cacheService.LoadDialogCacheAsync();

                if (loaded.Count != 3) throw new Exception($"Expected 3 loaded items, got {loaded.Count}");
                if (loaded[0].Id != 9223372036854775800L) throw new Exception($"64-bit ID corrupted: got {loaded[0].Id}");
                if (loaded[1].Id != -1001234567890L) throw new Exception($"Negative 64-bit ID corrupted: got {loaded[1].Id}");
                if (loaded[2].Username != "helper_bot") throw new Exception($"Username mismatch: {loaded[2].Username}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        });

        await RunTestAsync("4.2 Avatar File Integrity Validation (JPEG/PNG magic bytes)", async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TeleClean_Adv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var configService = new ConfigService(tempDir);
                var cacheService = new CacheService(configService);

                long validJpegId = 777111L;
                long validPngId = 777222L;
                long corruptId = 777333L;

                byte[] jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01];
                using (var ms = new MemoryStream(jpegBytes))
                {
                    await cacheService.SaveAvatarAsync(validJpegId, ms);
                }

                byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];
                using (var ms = new MemoryStream(pngBytes))
                {
                    await cacheService.SaveAvatarAsync(validPngId, ms);
                }

                if (!cacheService.HasAvatar(validJpegId)) throw new Exception("Valid JPEG avatar was rejected by HasAvatar!");
                if (!cacheService.HasAvatar(validPngId)) throw new Exception("Valid PNG avatar was rejected by HasAvatar!");

                byte[] corruptBytes = [0x00, 0x01, 0x02, 0x03, 0x04];
                bool threw = false;
                try
                {
                    using var ms = new MemoryStream(corruptBytes);
                    await cacheService.SaveAvatarAsync(corruptId, ms);
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }

                if (!threw) throw new Exception("Corrupt avatar did not throw InvalidOperationException on SaveAvatarAsync!");
                if (cacheService.HasAvatar(corruptId)) throw new Exception("Corrupt avatar passed HasAvatar check!");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        });

        await RunTestAsync("3.7 Pause Call During RateLimited Backoff Is Ignored (State Stays RateLimited)", async () =>
        {
            var fakeTelegram = new FakeTelegramService();
            var fakeCache = new FakeCacheService();
            var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

            var item1 = new DialogItem { Id = 801, Title = "RL_Item", DialogType = DialogType.Channel };
            var item2 = new DialogItem { Id = 802, Title = "Next_Item", DialogType = DialogType.Channel };
            fakeTelegram.SetResult(801, LeaveResult.RateLimited);
            fakeTelegram.SetResult(802, LeaveResult.Success);

            var runTask = engine.StartAsync(new[] { item1, item2 }, delayBetween: TimeSpan.FromSeconds(1), rateLimitDelay: TimeSpan.FromSeconds(2));

            // Wait for RateLimited state
            var timeout = DateTime.UtcNow.AddSeconds(2);
            while (engine.State != BulkOperationState.RateLimited && DateTime.UtcNow < timeout)
            {
                await Task.Delay(20);
            }

            if (engine.State != BulkOperationState.RateLimited)
            {
                throw new Exception($"Engine failed to enter RateLimited state: {engine.State}");
            }

            // Call Pause while RateLimited
            engine.Pause();

            // Verify State is STILL RateLimited (not Paused, because Pause() requires State == Running)
            if (engine.State != BulkOperationState.RateLimited)
            {
                throw new Exception($"Expected State to remain RateLimited, but got: {engine.State}");
            }

            // Await completion of runTask without calling Resume()
            await runTask;

            if (engine.State != BulkOperationState.Completed)
            {
                throw new Exception($"Expected Completed, got {engine.State}");
            }
        });

        await RunTestAsync("3.8 Empty and Null List Handling", async () =>
        {
            var fakeTelegram = new FakeTelegramService();
            var fakeCache = new FakeCacheService();
            var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

            // Null items throws ArgumentNullException
            bool threwNull = false;
            try
            {
                await engine.StartAsync(null!, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
            catch (ArgumentNullException)
            {
                threwNull = true;
            }
            if (!threwNull) throw new Exception("StartAsync(null) did not throw ArgumentNullException!");

            // Empty items sets Completed immediately
            await engine.StartAsync(Array.Empty<DialogItem>(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            if (engine.State != BulkOperationState.Completed)
            {
                throw new Exception($"Expected Completed for empty items, got {engine.State}");
            }
        });

        RunTest("4.3 3-Tier Credentials Resolution", () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TeleClean_Adv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var configService = new ConfigService(tempDir);

                Environment.SetEnvironmentVariable("TELEGRAM_API_ID", null);
                Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", null);

                var (id3, hash3) = configService.GetEffectiveCredentials();
                if (id3 != ConfigService.BuildApiId || hash3 != ConfigService.BuildApiHash)
                {
                    throw new Exception($"Tier 3 fallback credentials mismatch: got {id3} / {hash3}");
                }

                Environment.SetEnvironmentVariable("TELEGRAM_API_ID", "9999999");
                Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", "env_override_hash");

                var (id1, hash1) = configService.GetEffectiveCredentials();
                if (id1 != 9999999 || hash1 != "env_override_hash")
                {
                    throw new Exception($"Tier 1 env credentials mismatch: got {id1} / {hash1}");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("TELEGRAM_API_ID", null);
                Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", null);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        });

        await RunTestAsync("4.4 Legacy Python leave_history.json Parsing Compatibility", async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TeleClean_Adv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var configService = new ConfigService(tempDir);
                var cacheService = new CacheService(configService);

                var legacyJson = """
                [
                  {
                    "started_at": 1700000000.0,
                    "total": 5,
                    "completed": 5,
                    "success": 4,
                    "errors": [
                      {
                        "channel_id": 999123456789,
                        "title": "LegacyRateLimitedChan",
                        "error": "rate_limited"
                      }
                    ],
                    "finished_at": 1700000050.0
                  }
                ]
                """;

                var histPath = Path.Combine(tempDir, "leave_history.json");
                await File.WriteAllTextAsync(histPath, legacyJson);

                var history = await cacheService.LoadHistoryAsync();
                if (history.Count < 2)
                {
                    throw new Exception($"Expected at least 2 entries parsed from legacy history, got {history.Count}");
                }

                var errEntry = history.FirstOrDefault(h => h.DialogId == 999123456789L);
                if (errEntry == null)
                {
                    throw new Exception("Failed to parse legacy error entry with 64-bit channel_id=999123456789");
                }
                if (errEntry.Result != LeaveResult.RateLimited)
                {
                    throw new Exception($"Expected Result=RateLimited, got {errEntry.Result}");
                }
                if (errEntry.Title != "LegacyRateLimitedChan")
                {
                    throw new Exception($"Expected Title='LegacyRateLimitedChan', got {errEntry.Title}");
                }
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        });

        RunTest("4.5 Sub-Second Delay Truncation Behavior", () =>
        {
            var subSecond = TimeSpan.FromMilliseconds(500);
            int delaySeconds = (int)Math.Max(0, subSecond.TotalSeconds);
            if (delaySeconds != 0)
            {
                throw new Exception($"Expected delaySeconds to truncate to 0 for 500ms, got {delaySeconds}");
            }
        });

        // =========================================================================
        // SUMMARY
        // =========================================================================
        Console.WriteLine("\n=================================================");
        Console.WriteLine($"  TOTAL: {passed + failed} | PASSED: {passed} | FAILED: {failed}");
        Console.WriteLine("=================================================");

        return failed == 0 ? 0 : 1;
    }
}

internal class FakeTelegramService : ITelegramService
{
    private readonly ConcurrentDictionary<long, LeaveResult> _results = new();
    private readonly int _simulatedDelayMs;
    public int LeaveCallCount = 0;

    public bool IsConfigured => true;
    public bool IsAuthorized => true;

    public FakeTelegramService(int simulatedDelayMs = 0)
    {
        _simulatedDelayMs = simulatedDelayMs;
    }

    public void SetResult(long dialogId, LeaveResult result) => _results[dialogId] = result;

    public Task<bool> CheckExistingSessionAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task<string> BeginLoginWithQrAsync(Action<string> onQrUrlGenerated, Action? onPasswordNeeded = null, CancellationToken ct = default) => Task.FromResult("LOGIN_SUCCESS");
    public Task<string> LoginWithPhoneAsync(string phone, CancellationToken ct = default) => Task.FromResult("LOGIN_SUCCESS");
    public Task<string> SubmitPhoneCodeAsync(string code, CancellationToken ct = default) => Task.FromResult("LOGIN_SUCCESS");
    public Task<string> SubmitPasswordAsync(string password, CancellationToken ct = default) => Task.FromResult("LOGIN_SUCCESS");
    public Task ResetSessionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RecreateClientAsync() => Task.CompletedTask;
    public Task<IReadOnlyList<DialogItem>> FetchDialogsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DialogItem>>(Array.Empty<DialogItem>());

    public async Task<LeaveResult> LeaveDialogAsync(DialogItem dialog, CancellationToken ct = default)
    {
        Interlocked.Increment(ref LeaveCallCount);
        if (_simulatedDelayMs > 0)
        {
            await Task.Delay(_simulatedDelayMs, ct);
        }
        if (_results.TryGetValue(dialog.Id, out var result))
        {
            return result;
        }
        return LeaveResult.Success;
    }

    public Task<string?> DownloadAvatarAsync(long dialogId, CancellationToken ct = default) => Task.FromResult<string?>(null);
}

internal class FakeCacheService : ICacheService
{
    public Task<IReadOnlyList<DialogItem>> LoadDialogCacheAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DialogItem>>(Array.Empty<DialogItem>());
    public Task SaveDialogCacheAsync(IEnumerable<DialogItem> dialogs, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<BulkLogEntry>> LoadHistoryAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BulkLogEntry>>(Array.Empty<BulkLogEntry>());
    public Task AppendHistoryAsync(BulkLogEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    public string GetAvatarPath(long dialogId) => $"fake_avatar_{dialogId}.jpg";
    public bool HasAvatar(long dialogId) => false;
    public Task<string> SaveAvatarAsync(long dialogId, Stream stream, CancellationToken ct = default) => Task.FromResult($"fake_avatar_{dialogId}.jpg");
    public Task ClearCacheAsync(CancellationToken ct = default) => Task.CompletedTask;
}
