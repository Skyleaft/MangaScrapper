using System.Text.RegularExpressions;

namespace MangaScrapper.Infrastructure.Utils;

public static class StringHelper
{
    public static bool IsSimilar(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        // Fungsi lokal untuk membersihkan string
        string Clean(string input) => 
            Regex.Replace(input.ToLower(), @"[^\w]", ""); 

        string cleanA = Clean(a);
        string cleanB = Clean(b);

        // Cek hubungan timbal balik
        return cleanA.Contains(cleanB) || cleanB.Contains(cleanA);
    }
}