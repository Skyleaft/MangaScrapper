using MangaScrapper.Shared.Models;

namespace MangaScrapper.Features.Manga.UpdateManga;

public class Request
{
    public Guid Id { get; set; }
    public int MalId { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string>? Genres { get; set; }
    public string? Description { get; set; }
    public double? Rating { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? Status { get; set; }
    public int TotalView { get; set; }
}
