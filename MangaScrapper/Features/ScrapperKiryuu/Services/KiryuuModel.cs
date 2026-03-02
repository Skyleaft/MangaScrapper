using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.ScrapperKiryuu.Services;

public class KiryuuModel
{
    
}

public class KiryuuManga
{
    public int Id { get; set; }
    public string? Thumbnail { get; set; }
    public string? Title { get; set; }
    public string? Link { get; set; }
    public string? Status { get; set; }
    public string? Description { get; set; }
    public string Author { get; set; }
    public string Artist { get; set; }
    public double Rating { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<ChapterDocument>? Chapters { get; set; }
}
