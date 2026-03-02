namespace MangaScrapper.Features.ScrapperKomiku.ScrapManga;

public class Request
{
    public string MangaUrl { get; set; } = string.Empty;
    public bool ScrapChapterPages { get; set; } = true;
}