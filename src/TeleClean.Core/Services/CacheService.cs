using System.Text.Json;
using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;

namespace TeleClean.Core.Services;

/// <summary>
/// Thread-safe service for disk caching of dialog metadata, avatars, and bulk operation history.
/// </summary>
public sealed class CacheService : ICacheService
{
    private readonly IConfigService _configService;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly SemaphoreSlim _historyLock = new(1, 1);
    private readonly SemaphoreSlim _avatarLock = new(1, 1);

    public CacheService(IConfigService configService)
    {
        _configService = configService;
    }

    private string GetCacheDirectory() => _configService.GetDataDirectory();

    private string GetDialogCachePath() => Path.Combine(GetCacheDirectory(), "channel_cache.json");

    private string GetHistoryJournalPath() => Path.Combine(GetCacheDirectory(), "leave_history.jsonl");

    private string GetHistoryPath() => Path.Combine(GetCacheDirectory(), "leave_history.json");

    private string GetAvatarsDirectory()
    {
        var dir = _configService.CurrentConfig.AvatarCacheDir;
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Path.Combine(GetCacheDirectory(), "avatars");
        }
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public async Task<IReadOnlyList<DialogItem>> LoadDialogCacheAsync(CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            var path = GetDialogCachePath();
            if (!File.Exists(path))
            {
                return Array.Empty<DialogItem>();
            }

            var json = await File.ReadAllTextAsync(path, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<DialogItem>();
            }

            var items = JsonSerializer.Deserialize<List<DialogItem>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return items ?? (IReadOnlyList<DialogItem>)Array.Empty<DialogItem>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Array.Empty<DialogItem>();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task SaveDialogCacheAsync(IEnumerable<DialogItem> dialogs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dialogs);

        await _cacheLock.WaitAsync(ct);
        try
        {
            var path = GetDialogCachePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(dialogs, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await AtomicFile.WriteTextAsync(path, json, ct);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<IReadOnlyList<BulkLogEntry>> LoadHistoryAsync(CancellationToken ct = default)
    {
        await _historyLock.WaitAsync(ct);
        try
        {
            var result = (await LoadLegacyHistoryAsync(ct)).ToList();
            var path = GetHistoryJournalPath();
            if (!File.Exists(path)) return result;

            using var reader = new StreamReader(path);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<BulkLogEntry>(line);
                    if (entry != null) result.Add(entry);
                }
                catch (JsonException)
                {
                    // A partial last write must not hide preceding or subsequent complete records.
                }
            }
            return result;
        }
        finally { _historyLock.Release(); }
    }

    private async Task<IReadOnlyList<BulkLogEntry>> LoadLegacyHistoryAsync(CancellationToken ct = default)
    {
        try
        {
            var path = GetHistoryPath();
            if (!File.Exists(path))
            {
                return Array.Empty<BulkLogEntry>();
            }

            var json = await File.ReadAllTextAsync(path, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<BulkLogEntry>();
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<BulkLogEntry>();
            }

            var results = new List<BulkLogEntry>();
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (elem.TryGetProperty("dialog_id", out _) || elem.TryGetProperty("dialogId", out _))
                    {
                        var item = JsonSerializer.Deserialize<BulkLogEntry>(elem.GetRawText(), new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (item != null)
                        {
                            if (elem.TryGetProperty("dialogId", out var camelId)) item.DialogId = camelId.GetInt64();
                            results.Add(item);
                        }
                    }
                    else if (elem.TryGetProperty("started_at", out var startedAtProp))
                    {
                        double timestampSeconds = startedAtProp.GetDouble();
                        var time = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestampSeconds * 1000)).UtcDateTime;
                        int total = elem.TryGetProperty("total", out var tp) ? tp.GetInt32() : 0;
                        int success = elem.TryGetProperty("success", out var sp) ? sp.GetInt32() : 0;

                        if (elem.TryGetProperty("errors", out var errorsElem) && errorsElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var err in errorsElem.EnumerateArray())
                            {
                                long chId = err.TryGetProperty("channel_id", out var chIdProp) ? chIdProp.GetInt64() : 0;
                                string chTitle = err.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;
                                string errStr = err.TryGetProperty("error", out var errProp) ? errProp.GetString() ?? string.Empty : string.Empty;
                                var res = string.Equals(errStr, "rate_limited", StringComparison.OrdinalIgnoreCase) ? LeaveResult.RateLimited : LeaveResult.Error;
                                results.Add(new BulkLogEntry(time, chId, chTitle, res, errStr));
                            }
                        }

                        results.Add(new BulkLogEntry(time, 0, "Сессия", LeaveResult.Success, $"Всего: {total}, Успешно: {success}"));
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentOutOfRangeException)
                {
                    // Preserve readable legacy entries even if another record has incompatible fields.
                }
            }

            return results;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Array.Empty<BulkLogEntry>();
        }
    }

    public async Task AppendHistoryAsync(BulkLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var record = JsonSerializer.Serialize(entry);
        await _historyLock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(GetCacheDirectory());
            // The leading newline isolates any partial record left by an interrupted process.
            await File.AppendAllTextAsync(GetHistoryJournalPath(), "\n" + record + "\n", ct);
        }
        finally { _historyLock.Release(); }
    }

    public string GetAvatarPath(long dialogId)
    {
        return Path.Combine(GetAvatarsDirectory(), $"{dialogId}.jpg");
    }

    public bool HasAvatar(long dialogId)
    {
        return IsValidAvatar(GetAvatarPath(dialogId));
    }

    private static bool IsValidAvatar(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var fi = new FileInfo(path);
            if (fi.Length < 16)
            {
                // Corrupt / truncated file - remove so it can be re-fetched
                File.Delete(path);
                return false;
            }

            // Verify header bytes: JPEG (FF D8 FF) or PNG (89 50 4E 47)
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[4];
            int read = fs.Read(header);
            if (read < 4)
            {
                fs.Dispose();
                File.Delete(path);
                return false;
            }

            bool isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            bool isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;

            if (!isJpeg && !isPng)
            {
                fs.Dispose();
                File.Delete(path);
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> SaveAvatarAsync(long dialogId, Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var path = GetAvatarPath(dialogId);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await _avatarLock.WaitAsync(ct);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var fileStream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                await stream.CopyToAsync(fileStream, ct);
            }

            if (!IsValidAvatar(temporary))
                throw new InvalidOperationException($"Saved avatar for {dialogId} failed integrity validation.");

            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally
        {
            _avatarLock.Release();
            File.Delete(temporary);
        }
    }

    public async Task ClearAvatarCacheAsync(CancellationToken ct = default)
    {
        await _avatarLock.WaitAsync(ct);
        try
        {
            foreach (var file in Directory.EnumerateFiles(GetAvatarsDirectory(), "*.jpg"))
                File.Delete(file);
        }
        finally { _avatarLock.Release(); }
    }

    public async Task ClearCacheAsync(CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            var path = GetDialogCachePath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
