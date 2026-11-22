namespace TeleClean.Core.Services;

// Replacing the destination only after the complete write preserves the last valid snapshot.
internal static class AtomicFile
{
    public static async Task WriteTextAsync(string path, string contents, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, contents, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
