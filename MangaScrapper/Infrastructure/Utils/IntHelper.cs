using System.Globalization;
using System.Text.RegularExpressions;

namespace MangaScrapper.Infrastructure.Utils;

public static class IntHelper
{
    public static int ParseCount(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return 0;

        input = input.Trim().ToLowerInvariant();

        // ambil angka (support decimal)
        var match = Regex.Match(input, @"[\d\.]+");
        if (!match.Success)
            return 0;

        if (!double.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            return 0;

        // multiplier
        if (input.EndsWith("k"))
            number *= 1_000;
        else if (input.EndsWith("m"))
            number *= 1_000_000;
        else if (input.EndsWith("b"))
            number *= 1_000_000_000;

        return (int)Math.Round(number);
    }
}