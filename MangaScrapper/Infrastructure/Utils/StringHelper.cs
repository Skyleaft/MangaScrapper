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
    
    public static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a))
            return b?.Length ?? 0;

        if (string.IsNullOrEmpty(b))
            return a.Length;

        int[,] matrix = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= b.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(
                        matrix[i - 1, j] + 1,     // delete
                        matrix[i, j - 1] + 1      // insert
                    ),
                    matrix[i - 1, j - 1] + cost   // replace
                );
            }
        }

        return matrix[a.Length, b.Length];
    }
    
    public static double CalculateSimilarity(string a, string b)
    {
        int distance = LevenshteinDistance(a.ToLower(), b.ToLower());
        int maxLength = Math.Max(a.Length, b.Length);

        if (maxLength == 0)
            return 1.0;

        return 1.0 - (double)distance / maxLength;
    }
    
    public static string NormalizeTitle(string text)
    {
        text = text.ToLower();

        // remove text inside ()
        text = Regex.Replace(text, @"\([^)]*\)", "");

        // remove special characters
        text = Regex.Replace(text, @"[^a-z0-9\s]", "");

        return text.Trim();
    }
    
}