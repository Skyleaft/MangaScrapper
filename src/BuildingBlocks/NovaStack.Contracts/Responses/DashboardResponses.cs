namespace NovaStack.Contracts.Responses;

public record ScrapStatsResponse(DateTime Date, long TotalScrap);

public record DashboardStatisticResponse(
    long TotalManga,
    long TotalSourceProvider,
    long ScrappedToday,
    long ScrappedThisMonth,
    long TotalQueue,
    long TotalUnlinkedMetadata,
    long TotalUnavailableMangaChapter,
    long TotalStorageUsed,
    List<ScrapStatsResponse> MonthlyScrap);

public record StorageSyncReportResponse(
    int ProcessedMangasCount,
    int UpdatedMangasCount,
    long TotalThumbnailSize,
    long TotalPagesSize,
    List<string> Errors)
{
    public long TotalSize => TotalThumbnailSize + TotalPagesSize;
}
