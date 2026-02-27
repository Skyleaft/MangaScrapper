namespace MangaScrapper.Infrastructure.Services;

public class ScrapperSettings
{
    public int MaxParallelDownloads { get; set; } = 5;
    public string ImageStoragePath { get; set; } = "images";
}
