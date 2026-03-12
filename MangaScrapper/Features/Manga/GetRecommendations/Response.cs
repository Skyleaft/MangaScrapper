using MangaScrapper.Features.Manga.GetPaged;

namespace MangaScrapper.Features.Manga.GetRecommendations;

public class Response
{
    public List<MangaSummary> Items { get; set; } = new();
}
