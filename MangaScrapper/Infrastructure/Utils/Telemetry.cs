using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MangaScrapper.Infrastructure.Utils;

public static class Telemetry
{
    public const string ServiceName = "MangaScrapper";
    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> MangaScrapedCounter = Meter.CreateCounter<long>("manga.scraped.count", "Total number of mangas scraped");
    public static readonly Counter<long> ChaptersScrapedCounter = Meter.CreateCounter<long>("chapter.scraped.count", "Total number of chapters scraped");
    public static readonly Counter<long> PagesDownloadedCounter = Meter.CreateCounter<long>("page.downloaded.count", "Total number of pages downloaded");
    public static readonly Counter<long> ErrorsCounter = Meter.CreateCounter<long>("scrapper.errors.count", "Total number of scraping errors");
}
