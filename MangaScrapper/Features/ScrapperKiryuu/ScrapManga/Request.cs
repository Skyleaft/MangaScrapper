namespace MangaScrapper.Features.ScrapperKiryuu.ScrapManga;

public class Request
{
    public string MangaUrl { get; set; } = string.Empty;
    public bool ScrapChapterPages { get; set; }
}