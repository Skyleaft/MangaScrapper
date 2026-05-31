using FastEndpoints;
using Microsoft.AspNetCore.StaticFiles;

namespace MangaScrapper.Features.Images;

public class ProxyRequest
{
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Proxies an external image URL through the server so the browser never
/// makes a direct cross-origin request (avoids ERR_BLOCKED_BY_ORB / CORS issues).
/// </summary>
public class ProxyEndpoint(IHttpClientFactory httpClientFactory) : Endpoint<ProxyRequest>
{
    public override void Configure()
    {
        Get("/api/images/proxy");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ProxyRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Url) || !Uri.TryCreate(r.Url, UriKind.Absolute, out var uri))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Only allow http/https for safety
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient("ImageProxy");
            using var response = await client.GetAsync(r.Url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            // Detect content type from response header or file extension
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrEmpty(contentType) || !contentType.StartsWith("image/"))
            {
                new FileExtensionContentTypeProvider().TryGetContentType(uri.LocalPath, out contentType);
                contentType ??= "image/jpeg";
            }

            HttpContext.Response.ContentType = contentType;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await stream.CopyToAsync(HttpContext.Response.Body, ct);
        }
        catch
        {
            await Send.NotFoundAsync(ct);
        }
    }
}
