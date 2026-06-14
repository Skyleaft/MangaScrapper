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
        if (segments.Length > 0 && segments.Last().Contains('=') && !segments.Last().Contains("format=auto"))
        {
            var newPath = string.Concat(segments.Take(segments.Length - 1));
            return $"{uri.Scheme}://{uri.Host}{newPath}";
        }

        return url;
    }
    
    public static string RemoveQueryString(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty
        };

        return builder.Uri.ToString();
    }
}