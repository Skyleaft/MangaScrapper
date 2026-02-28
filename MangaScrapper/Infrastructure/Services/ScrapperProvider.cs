namespace MangaScrapper.Infrastructure.Services;

public class ScrapperProvider
{
    public string ProviderName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public MangaSelectorConfig MangaSelectors { get; set; } = new();
    public ChapterSelectorConfig ChapterSelectors { get; set; } = new();
    public PageSelectorConfig PageSelectors { get; set; } = new();
}

public class MangaSelectorConfig
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Genres { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
}

public class ChapterSelectorConfig
{
    public string Rows { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string ChapterText { get; set; } = string.Empty;
    public string Views { get; set; } = string.Empty;
    public string UploadDate { get; set; } = string.Empty;
}

public class PageSelectorConfig
{
    public string Images { get; set; } = string.Empty;
}
