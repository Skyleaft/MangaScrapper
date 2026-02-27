using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.Manga.GetManga;

public class Response : MangaDocument
{
    public new List<ChapterResponse> Chapters { get; set; } = new();
}

public class ChapterResponse
{
    public Guid Id { get; set; }
    public double Number { get; set; }
    public string? Link { get; set; }
    public int TotalView { get; set; }
    public DateTime UploadDate { get; set; }
    // Pages are excluded here to optimize the response
}
