namespace MangaScrapper.Shared.Models;

public class StorageSyncReport
{
    public int ProcessedMangasCount { get; set; }
    public int UpdatedMangasCount { get; set; }
    public long TotalThumbnailSize { get; set; }
    public long TotalPagesSize { get; set; }
    public long TotalSize => TotalThumbnailSize + TotalPagesSize;
    public List<string> Errors { get; set; } = new();
}
