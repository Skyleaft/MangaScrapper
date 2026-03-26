using HtmlAgilityPack;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Infrastructure.Services;

public interface IScrapperService
{
    Task<HtmlDocument> GetHtml(string url, string? query = null, MultipartFormDataContent? formData = null, CancellationToken ct = default);
    Task<(string path, long size)> DownloadAndConvertToWebP(string mangaTitle, string chapterNumber, string imageUrl, int index, CancellationToken ct = default);
    Task<(string path, long size)> DownloadThumbnailAndConvertToWebP(string mangaTitle, string imageUrl, CancellationToken ct = default);
    string GetCleanTitle(string title);
    Task<JikanMangaItem?> GetMangaInfo(string title, string type, CancellationToken ct = default);
    Task<JikanMangaItem?> GetMangaInfoById(int malId, CancellationToken ct = default);
    Task<MangaDocument> UpdateMangaDocument(MangaDocument manga, CancellationToken ct = default);
    Task<MangaDocument> ExtractManga(string url, CancellationToken ct, bool scrapChapters = true);
    Task<ChapterDocument> GetChapterPage(string mangaTitle, ChapterDocument chapter, CancellationToken ct = default);
    Task QueueChapterScraping(Guid mangaId, string mangaTitle, ChapterDocument chapter);
    Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct);
    Task<List<ChapterDocument>> GetAllChapters(string url, CancellationToken ct = default);
    Task<List<PageDocument>> GetAllPages(string url, CancellationToken ct = default);
    Task<List<ScrapperProvider>> GetAllProvider();
}
