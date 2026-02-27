namespace MangaScrapper.Features.Manga.GetChaptersPage;

public class Request
{
    public Guid MangaId { get; set; }
    public double Chapter { get; set; }
}