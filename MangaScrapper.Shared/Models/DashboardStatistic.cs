namespace MangaScrapper.Shared.Models;

public class DashboardStatistic
{
    public long TotalManga { get; set; }
    public long TotalSourceProvider { get; set; }
    public long ScrappedToday { get; set; }
    public long ScrappedThisMonth { get; set; }
    public long TotalQueue { get; set; }
    public long TotalUnlinkedMetadata { get; set; }
    public long TotalUnavailableMangaChapter { get; set; }
    public long TotalStorageUsed { get; set; }
    public List<ScrapStats> MonthlyScrap { get; set; } = new();
}