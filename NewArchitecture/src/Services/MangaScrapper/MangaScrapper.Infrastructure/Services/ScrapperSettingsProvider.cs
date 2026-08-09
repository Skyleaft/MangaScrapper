using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Infrastructure.Services;

public sealed class ScrapperSettingsProvider : IScrapperSettingsProvider
{
    private readonly string _imageStoragePath;

    public ScrapperSettingsProvider(IOptions<ScrapperSettings> options)
    {
        var raw = options.Value.ImageStoragePath;
        _imageStoragePath = Path.IsPathRooted(raw)
            ? raw
            : Path.Combine(Directory.GetCurrentDirectory(), raw);
    }

    public string ImageStoragePath => _imageStoragePath;
}
