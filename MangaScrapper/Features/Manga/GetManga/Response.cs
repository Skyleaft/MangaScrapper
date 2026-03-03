using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.Manga.GetManga;

public class Response
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
}