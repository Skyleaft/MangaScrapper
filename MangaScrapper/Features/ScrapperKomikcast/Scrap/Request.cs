namespace MangaScrapper.Features.ScrapperKomikcast.Scrap;

public class Request
{
    public string MangaUrl { get; set; } = string.Empty;
    public bool ScrapChapterPages { get; set; }
    public string? LinkId { get; set; }
}