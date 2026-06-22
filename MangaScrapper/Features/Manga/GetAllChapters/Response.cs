namespace MangaScrapper.Features.Manga.GetAllChapters;

public class Response
{
    public Guid Id { get; set; }
    public double Number { get; set; }
    public int TotalView { get; set; }
    public DateTime UploadDate { get; set; }
    public string? ChapterProvider { get; set; }
    public string? ChapterProviderIcon { get; set; }
    public string Language { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public bool IsChapterAvailable { get; set; }
}