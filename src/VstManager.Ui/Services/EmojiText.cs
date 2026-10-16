using System.Globalization;

namespace VstManager.Ui.Services;

/// <summary>
/// Keeps a category icon field to exactly one emoji. Works on text elements (grapheme
/// clusters), so multi-codepoint emoji — flags, skin tones, ZWJ families, keycaps — count as one.
/// </summary>
public static class EmojiText
{
    /// <summary>The last emoji in <paramref name="text"/>, or empty when it holds none.</summary>
    public static string LastEmoji(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var last = string.Empty;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            if (IsEmoji(element))
            {
                last = element;
            }
        }
        return last;
    }

    private static bool IsEmoji(string element)
    {
        foreach (var rune in element.EnumerateRunes())
        {
            var v = rune.Value;
            if (v is 0xFE0F or 0x20E3                // emoji presentation selector, keycap
                || v is >= 0x1F000 and <= 0x1FAFF    // pictographs, emoticons, transport, flags…
                || v is >= 0x2600 and <= 0x27BF      // misc symbols, dingbats (☀ ✂ ❔)
                || v is >= 0x2300 and <= 0x23FF      // misc technical (⌚ ⏰ ⏳)
                || v is >= 0x2B00 and <= 0x2BFF      // arrows/shapes with emoji forms (⭐ ⬛)
                || v is 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139
                    or 0x3030 or 0x303D or 0x3297 or 0x3299)
            {
                return true;
            }
        }
        return false;
    }
}
