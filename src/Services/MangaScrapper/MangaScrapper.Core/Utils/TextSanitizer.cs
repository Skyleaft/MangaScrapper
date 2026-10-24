using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;

namespace MangaScrapper.Core.Utils;

public static class TextSanitizer
{
    private static readonly Regex ScriptOrStyleTagRegex =
        new(@"<(script|style)[^>]*>[\s\S]*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LineBreakRegex =
        new(@"<(?:br\s*/?|/p|/div|/li|/tr|p\b[^>]*|div\b[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HtmlTagRegex =
        new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex NumericEntityRegex =
        new(@"&#(?:x([0-9a-fA-F]+)|(\d+));?", RegexOptions.Compiled);

    private static readonly Regex NamedEntityWithoutSemicolonRegex =
        new(@"&(nbsp|amp|quot|apos|lt|gt|ldquo|rdquo|lsquo|rsquo|hellip|ndash|mdash)(?![a-zA-Z0-9;])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MultiNewlineRegex =
        new(@"\n{3,}", RegexOptions.Compiled);

    private static readonly Regex MultiSpaceRegex =
        new(@"[^\S\r\n]{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes an HtmlNode's description content, converting paragraph and break tags
    /// to line breaks, stripping HTML tags, resolving broken/unclosed HTML entities (e.g. &#8220),
    /// and decoding HTML entities into clean text.
    /// </summary>
    public static string SanitizeDescription(HtmlNode? node)
    {
        if (node == null) return string.Empty;

        // Prefer InnerHtml so block/break tags (<p>, <br>) can be converted to proper newlines.
        // Fallback to InnerText if InnerHtml is empty or unavailable.
        var raw = !string.IsNullOrWhiteSpace(node.InnerHtml) ? node.InnerHtml : node.InnerText;
        return SanitizeDescription(raw);
    }

    /// <summary>
    /// Sanitizes a raw description string, stripping HTML tags, resolving unclosed/broken
    /// HTML entities (such as &#8220 without semicolon), decoding HTML entities (including
    /// multi-level/double encoded ones), and normalizing whitespace.
    /// </summary>
    public static string SanitizeDescription(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = raw;

        // 1. Remove script and style tags and their inner content
        text = ScriptOrStyleTagRegex.Replace(text, string.Empty);

        // 2. Convert common block & break tags to newlines
        text = LineBreakRegex.Replace(text, "\n");

        // 3. Remove all remaining HTML tags
        text = HtmlTagRegex.Replace(text, string.Empty);

        // 4. Decode HTML entities (including numeric entities with or without semicolons and double encoded entities)
        text = DecodeHtmlEntities(text);

        // 5. Clean and normalize spaces and line breaks
        return NormalizeWhitespaceAndBreaks(text);
    }

    /// <summary>
    /// Lightweight sanitizer for one-line text fields (such as titles, authors, genres).
    /// </summary>
    public static string SanitizeSingleLine(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = HtmlTagRegex.Replace(raw, string.Empty);
        text = DecodeHtmlEntities(text);

        text = text.Replace('\u00A0', ' ')
                   .Replace('\u200B', ' ')
                   .Replace('\uFEFF', ' ')
                   .Replace('\r', ' ')
                   .Replace('\n', ' ');

        text = MultiSpaceRegex.Replace(text, " ");
        return text.Trim();
    }

    private static string DecodeHtmlEntities(string input)
    {
        var current = input;
        for (int i = 0; i < 3; i++)
        {
            // Fix named entities missing semicolon (e.g. &nbsp or &ldquo without semicolon)
            var normalized = NamedEntityWithoutSemicolonRegex.Replace(current, "&$1;");

            // Replace numeric entities directly (e.g. &#8220; or &#8220 -> “)
            normalized = NumericEntityRegex.Replace(normalized, match =>
            {
                try
                {
                    if (match.Groups[1].Success && int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexCode))
                    {
                        if (IsValidCodePoint(hexCode))
                            return char.ConvertFromUtf32(hexCode);
                    }
                    else if (match.Groups[2].Success && int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decCode))
                    {
                        if (IsValidCodePoint(decCode))
                            return char.ConvertFromUtf32(decCode);
                    }
                }
                catch
                {
                    // Fall back to original match if invalid code point
                }
                return match.Value;
            });

            // Decode remaining named entities (e.g. &amp;, &quot;, &lt;, &gt;)
            var decoded = HttpUtility.HtmlDecode(normalized);

            if (decoded == current)
                break;

            current = decoded;
        }

        return current;
    }

    private static bool IsValidCodePoint(int code)
    {
        return code >= 0 && code <= 0x10FFFF && !(code >= 0xD800 && code <= 0xDFFF);
    }

    private static string NormalizeWhitespaceAndBreaks(string input)
    {
        // Replace non-breaking and zero-width spaces
        var cleaned = input.Replace('\u00A0', ' ')
                           .Replace('\u200B', ' ')
                           .Replace('\u200C', ' ')
                           .Replace('\u200D', ' ')
                           .Replace('\uFEFF', ' ');

        // Standardize newlines
        cleaned = cleaned.Replace("\r\n", "\n").Replace('\r', '\n');

        // Split into lines, trim each line and remove redundant horizontal spaces
        var lines = cleaned.Split('\n')
                           .Select(line => MultiSpaceRegex.Replace(line.Trim(), " "))
                           .ToArray();

        var result = string.Join("\n", lines);

        // Collapse 3 or more consecutive newlines into 2 (paragraph spacing)
        result = MultiNewlineRegex.Replace(result, "\n\n");

        return result.Trim();
    }
}
