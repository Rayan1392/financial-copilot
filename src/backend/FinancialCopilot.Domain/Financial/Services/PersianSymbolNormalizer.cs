namespace FinancialCopilot.Domain.Financial.Services;

/// <summary>
/// Normalizes Persian/Arabic ticker symbols for consistent cross-provider comparison.
/// Strips invisible Unicode control characters and maps Arabic look-alikes to their
/// canonical Persian equivalents so that symbols from different data sources resolve
/// to the same string even when one vendor's feed contains Arabic characters or
/// invisible directional markers.
/// </summary>
public static class PersianSymbolNormalizer
{
    // Invisible / directional Unicode characters that appear in some Iranian financial feeds.
    private static readonly char[] InvisibleChars =
    [
        '‌', // ZWNJ — Zero Width Non-Joiner
        '‍', // ZWJ  — Zero Width Joiner
        '‏', // RLM  — Right-to-Left Mark
        '‫', // RLE  — Right-to-Left Embedding
        '﻿', // BOM  — Byte Order Mark / Zero Width No-Break Space
        '­', // SHY  — Soft Hyphen
    ];

    /// <summary>
    /// Returns the normalized form of <paramref name="input"/>:
    /// trimmed, stripped of invisible characters, Arabic Ye/Kaf mapped to Persian,
    /// and compacted so spacing/punctuation variants normalize to the same lookup key.
    /// Returns an empty string for null or whitespace-only input.
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = input.Trim();
        var result = new System.Text.StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (Array.IndexOf(InvisibleChars, ch) >= 0)
            {
                continue;
            }

            var mapped = ch switch
            {
                'ي' => 'ی', // Arabic Ye (ي) → Persian Ye (ی)
                'ك' => 'ک', // Arabic Kaf (ك) → Persian Kaf (ک)
                _ => ch
            };

            if (char.IsWhiteSpace(mapped) || char.IsPunctuation(mapped) || char.IsSeparator(mapped))
            {
                continue;
            }

            result.Append(mapped);
        }

        return result.ToString();
    }
}
