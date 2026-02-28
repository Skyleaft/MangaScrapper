namespace MangaScrapper.Features.ScrapperKomiku.Search;

public class SearchItem
{
    public string? Title { get; set; }
    public string DetailUrl { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string LastUpdate { get; set; } = string.Empty;
    public double LatestChapterNumber { get; set; }
}