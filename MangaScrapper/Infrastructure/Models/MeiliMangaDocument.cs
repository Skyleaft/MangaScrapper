namespace MangaScrapper.Infrastructure.Models;

public class MeiliMangaDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> Genres { get; set; } = new();
    public string? Description { get; set; }
    public string? Status { get; set; }
    public double? Rating { get; set; }
    public int Popularity { get; set; }
    public int TotalView { get; set; }
    public string? ImageUrl { get; set; } = string.Empty;
    public string? LocalImageUrl { get; set; }
    public int TotalChapters { get; set; }
    public double LatestChapterNumber { get; set; }
    public long CreatedAtTimestamp { get; set; }
    public long UpdatedAtTimestamp { get; set; }
}
