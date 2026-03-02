namespace MangaScrapper.Infrastructure.Models;

public class SearchRequest
{
    public string? Keyword { get; set; }
    public List<string>? Genres { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public int Page { get; set; } = 1; 
}