namespace MangaScrapper.Shared.Models;

public class DashboardStatistic
{
    public long TotalManga { get; set; }
    public long TotalSourceProvider { get; set; }
    public long ScrappedToday { get; set; }
    public long ScrappedThisWeek { get; set; }
    public long TotalQueue { get; set; }
    public long TotalUnlinkedMetadata { get; set; }
    public long TotalUnavailableMangaChapter { get; set; }
}