using FastEndpoints;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.Images;

public class Endpoint(IOptions<ScrapperSettings> settings) : Endpoint<Request>
{
    private readonly string _imageStoragePath = Path.IsPathRooted(settings.Value.ImageStoragePath) 
        ? settings.Value.ImageStoragePath 
        : Path.Combine(Directory.GetCurrentDirectory(), settings.Value.ImageStoragePath);

    public override void Configure()
    {
        Get("/api/images/{*FilePath}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var filePath = Path.Combine(_imageStoragePath, r.FilePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(filePath))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        new FileExtensionContentTypeProvider().TryGetContentType(filePath, out var contentType);
        
        await Send.FileAsync(new FileInfo(filePath), contentType ?? "application/octet-stream", cancellation: ct);
    }
}