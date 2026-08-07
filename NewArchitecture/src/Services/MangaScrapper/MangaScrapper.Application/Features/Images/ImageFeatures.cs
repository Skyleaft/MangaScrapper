using MangaScrapper.Application.Common.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MangaScrapper.Application.Features.Images;

/// <summary>
/// Serves local image files stored on disk.
/// Route: GET /api/images/{*filePath}
/// This endpoint does not go through MediatR — it is a pure file-serving
/// endpoint equivalent to the old FastEndpoints Endpoint&lt;Request&gt; handler.
/// </summary>
public sealed class ServeImageEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/images/{*filePath}", HandleAsync)
            .WithName("ServeImage")
            .WithSummary("Serve a local image file from disk storage")
            .WithTags("Images")
            .AllowAnonymous()
            .CacheOutput(p => p.Expire(TimeSpan.FromDays(3)));
    }

    private static async Task<IResult> HandleAsync(
        string filePath,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var rawPath = configuration["Scrapper:ImageStoragePath"] ?? "images";

        var imageStoragePath = Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.Combine(Directory.GetCurrentDirectory(), rawPath);

        var absolutePath = Path.Combine(
            imageStoragePath,
            filePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolutePath))
            return Results.NotFound();

        new FileExtensionContentTypeProvider()
            .TryGetContentType(absolutePath, out var contentType);

        var bytes = await File.ReadAllBytesAsync(absolutePath, ct);
        return Results.Bytes(bytes, contentType ?? "application/octet-stream");
    }
}
