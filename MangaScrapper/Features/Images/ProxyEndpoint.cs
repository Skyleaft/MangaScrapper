using FastEndpoints;
using Microsoft.AspNetCore.StaticFiles;
using System.Net.Http;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.Images;

public class ProxyRequest
{
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Proxies an external image URL through the server so the browser never
/// makes a direct cross-origin request (avoids ERR_BLOCKED_BY_ORB / CORS issues).
/// </summary>
public class ProxyEndpoint(IHttpClientFactory httpClientFactory, FlareSolverrService flareSolverrService) : Endpoint<ProxyRequest>
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
            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, r.Url);

            if (uri.Host.Contains("mangadex.org", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.UserAgent.Clear();
                request.Headers.TryAddWithoutValidation("User-Agent", "MangaScrapper/1.0");
            }

            // if (flareSolverrService.IsEnabled)
            // {
            //     // EnsureSessionAsync coalesces concurrent challenge-solve requests —
            //     // only one FlareSolverr round-trip happens per host; others wait and reuse.
            //     try { await flareSolverrService.EnsureSessionAsync(r.Url, ct); } catch { /* Ignore, proceed without session */ }
            //
            //     flareSolverrService.TryGetSession(uri.Host, out var userAgent, out var cookieHeader);
            //
            //     if (!string.IsNullOrEmpty(userAgent))
            //     {
            //         request.Headers.UserAgent.Clear();
            //         request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            //     }
            //     if (!string.IsNullOrEmpty(cookieHeader))
            //     {
            //         request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            //     }
            // }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

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
