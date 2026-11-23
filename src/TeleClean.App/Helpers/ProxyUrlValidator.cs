using System;

namespace TeleClean.App.Helpers;

/// <summary>
/// Validates and normalizes Telegram MTProxy connection links.
/// </summary>
public static class ProxyUrlValidator
{
    public static bool TryValidateAndNormalize(string? rawInput, out string? normalized, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            normalized = null;
            errorMessage = null;
            return true;
        }

        string candidate = rawInput.Trim();
        if (candidate.StartsWith("t.me/proxy?", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "https://" + candidate;
        }

        bool isTgScheme = candidate.StartsWith("tg://proxy?", StringComparison.OrdinalIgnoreCase);
        bool isHttpsScheme = candidate.StartsWith("https://t.me/proxy?", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("http://t.me/proxy?", StringComparison.OrdinalIgnoreCase);

        if (!isTgScheme && !isHttpsScheme)
        {
            normalized = null;
            errorMessage = "Укажите ссылку MTProxy вида tg://proxy?server=...&port=...&secret=... или https://t.me/proxy?...";
            return false;
        }

        int queryIndex = candidate.IndexOf('?');
        string query = queryIndex >= 0 && queryIndex < candidate.Length - 1
            ? candidate[(queryIndex + 1)..]
            : string.Empty;

        bool hasServer = false;
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0 || eq == pair.Length - 1) continue;
            string key = pair[..eq];
            string value = pair[(eq + 1)..];
            if (key.Equals("server", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                hasServer = true;
                break;
            }
        }

        if (!hasServer)
        {
            normalized = null;
            errorMessage = "В ссылке MTProxy не указан параметр server.";
            return false;
        }

        normalized = candidate;
        errorMessage = null;
        return true;
    }
}
