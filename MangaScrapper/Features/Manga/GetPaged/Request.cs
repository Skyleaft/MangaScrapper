namespace MangaScrapper.Features.Manga.GetPaged;

public class Request
{
    public string? Search { get; set; }
    public List<string>? Genres { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}