namespace MangaScrapper.Features.Manga.Delete_Chapter;

public class Request
{
    public Guid MangaId { get; set; }
    public Guid ChapterId { get; set; }
}