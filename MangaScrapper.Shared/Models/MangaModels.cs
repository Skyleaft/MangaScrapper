namespace MangaScrapper.Shared.Models;

public class MangaSummary
{
    public Guid Id { get; set; }
    public int MalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string>? Genres { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? LocalImageUrl { get; set; }
    public double? Rating { get; set; }
    public int Popularity { get; set; }
    public int Members { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Url { get; set; }
    public int TotalView { get; set; }
    public LatestChapterSummary LatestChapter { get; set; } = new();
}

public class LatestChapterSummary
{
    public Guid Id { get; set; }
    public double Number { get; set; }
    public int TotalView { get; set; }
    public string? ChapterProvider { get; set; }
    public string? ChapterProviderIcon { get; set; }
    public DateTime UploadDate { get; set; }
}

public class MangaListResponse
{
    public List<MangaSummary> Items { get; set; } = new();
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
