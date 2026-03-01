namespace MangaScrapper.Infrastructure.Utils;

public static class ThumbnailHelper
{
    public static string RemoveResizeParams(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        var uri = new Uri(url);
        var segments = uri.Segments;

        // Jika segment terakhir mengandung '=' biasanya itu resize params
        if (segments.Length > 0 && segments.Last().Contains("="))
        {
            var newPath = string.Concat(segments.Take(segments.Length - 1));
            return $"{uri.Scheme}://{uri.Host}{newPath}";
        }

        return url;
    }
}