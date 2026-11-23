using System;
using System.Collections.Generic;
using System.Globalization;

namespace TeleClean.App.Helpers;

/// <summary>
/// Pure helper for computing Unicode-safe avatar initials and deterministic palette indices.
/// </summary>
public static class AvatarInitialsHelper
{
    public const int PaletteSize = 7;

    /// <summary>
    /// Extracts up to two grapheme-safe uppercase initials from a dialog title.
    /// Multi-word or hyphenated titles yield two initials (e.g. "Архитектура города" -> "АГ");
    /// single-word titles yield one initial (e.g. "Telegram" -> "T").
    /// </summary>
    public static string GetInitials(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "?";
        }

        var parts = title.Split(new[] { ' ', '\t', '-', '_', '/', '·', '|' }, StringSplitOptions.RemoveEmptyEntries);
        var letters = new List<string>(2);

        foreach (var part in parts)
        {
            string? element = ExtractLeadingElement(part);
            if (!string.IsNullOrEmpty(element))
            {
                letters.Add(element);
                if (letters.Count == 2) break;
            }
        }

        if (letters.Count == 0)
        {
            string fallback = StringInfo.GetNextTextElement(title.TrimStart());
            return string.IsNullOrEmpty(fallback) ? "?" : fallback.ToUpperInvariant();
        }

        return string.Concat(letters);
    }

    /// <summary>
    /// Computes a deterministic palette bucket in [0, PaletteSize) using FNV-1a over the normalized title.
    /// </summary>
    public static int GetPaletteIndex(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        const uint offsetBasis = 2166136261u;
        const uint prime = 16777619u;

        uint hash = offsetBasis;
        string normalized = key.Trim().ToUpperInvariant();
        for (int i = 0; i < normalized.Length; i++)
        {
            hash ^= normalized[i];
            hash *= prime;
        }

        return (int)(hash % PaletteSize);
    }

    private static string? ExtractLeadingElement(string token)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(token);
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            if (element.Length == 0) continue;

            char first = element[0];
            if (char.IsLetterOrDigit(first))
            {
                return element.ToUpperInvariant();
            }
        }

        return null;
    }
}
