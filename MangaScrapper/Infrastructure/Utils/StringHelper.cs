using System.Text.RegularExpressions;

namespace MangaScrapper.Infrastructure.Utils;

public static class StringHelper
{
    public static bool IsSimilar(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;

        // 1. Normalize: Lowercase and remove punctuation using Regex
        string cleanA = Regex.Replace(a.ToLower(), @"[^\w\s]", "");
        string cleanB = Regex.Replace(b.ToLower(), @"[^\w\s]", "");

        // 2. Trim extra spaces
        cleanA = Regex.Replace(cleanA, @"\s+", " ").Trim();
        cleanB = Regex.Replace(cleanB, @"\s+", " ").Trim();

        // 3. Check if one is a substring of the other
        return cleanA.Contains(cleanB) || cleanB.Contains(cleanA);
    }
}