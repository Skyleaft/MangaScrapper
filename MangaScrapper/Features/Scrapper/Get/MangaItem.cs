namespace MangaScrapper.Features.Scrapper.Get;

public class MangaItem
{
    public string Title { get; set; }
    public string DetailUrl { get; set; }
    public string Slug { get; set; }
    public string ThumbnailUrl { get; set; }
    public string Type { get; set; }
    public string Genre { get; set; }
    public string UpdateRank { get; set; }
    public string ReaderCount { get; set; }
    public string LastUpdated { get; set; }
    public bool IsColored { get; set; }
    public string Description { get; set; }
    public string FirstChapterTitle { get; set; }
    public string FirstChapterUrl { get; set; }
    public string LatestChapterTitle { get; set; }
    public string LatestChapterUrl { get; set; }
}