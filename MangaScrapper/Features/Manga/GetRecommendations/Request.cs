namespace MangaScrapper.Features.Manga.GetRecommendations;

public class Request
{
    public List<Guid> ReadingHistoryIds { get; set; } = new();
    public int Limit { get; set; } = 10;
}
