using HtmlAgilityPack;
using MangaScrapper.Infrastructure.Mongo.Collections;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace MangaScrapper.Infrastructure.Services;

public class ScrapperService
{
    private readonly HttpClient _httpClient;
    private readonly string _imageStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "images");

    public ScrapperService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "minimal-api-httpclient-sample");
        Directory.CreateDirectory(_imageStoragePath);
    }

    public async Task<HtmlDocument> GetHtml(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var str = await response.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(str);
        return doc;
    }

    public async Task<ChapterDocument> GetChapterPage(ChapterDocument chapter)
    {
        var url = "https://komiku.org" + chapter.Link;
        var doc = await GetHtml(url);

        var imageNodes = doc.DocumentNode.SelectNodes("//div[@id='Baca_Komik']//img[@src]");
        if (imageNodes == null) return chapter;

        foreach (var imgNode in imageNodes)
        {
            var imageUrl = imgNode.GetAttributeValue("src", null);
            if (string.IsNullOrEmpty(imageUrl)) continue;

            var localPath = await DownloadAndConvertToWebP(imageUrl);
            chapter.Pages.Add(new PageDocument
            {
                ImageUrl = imageUrl,
                LocalImageUrl = localPath
            });
        }

        return chapter;
    }

    private async Task<string> DownloadAndConvertToWebP(string imageUrl)
    {
        var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
        var fileName = $"{Guid.NewGuid()}.webp";
        var filePath = Path.Combine(_imageStoragePath, fileName);

        using var image = Image.Load(imageBytes);
        await image.SaveAsync(filePath, new WebpEncoder());

        return filePath;
    }
}