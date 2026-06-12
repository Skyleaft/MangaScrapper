using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Infrastructure.Services;

public class FlareSolverrService
{
    private readonly HttpClient _httpClient;
    private readonly FlareSolverrSettings _settings;

    public FlareSolverrService(HttpClient httpClient, IOptions<FlareSolverrSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public bool IsEnabled => _settings.Enabled;

    public async Task<string> GetHtmlAsync(string url, HttpContent? content = default, CancellationToken ct = default)
    {
        var requestBody = new FlareSolverrRequest
        {
            Url = url,
            MaxTimeout = 60000
        };

        if (content != null)
        {
            requestBody.Cmd = "request.post";
            requestBody.PostData = await content.ReadAsStringAsync(ct);
        }
        else
        {
            requestBody.Cmd = "request.get";
        }

        var response = await _httpClient.PostAsJsonAsync("/v1", requestBody, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<FlareSolverrResponse>(cancellationToken: ct);
        if (result == null || result.Status != "ok" || result.Solution == null)
        {
            throw new HttpRequestException($"FlareSolverr request failed: {result?.Message ?? "Unknown error"}");
        }

        return result.Solution.Response;
    }
}

public class FlareSolverrRequest
{
    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = "request.get";

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("postData")]
    public string? PostData { get; set; }

    [JsonPropertyName("maxTimeout")]
    public int MaxTimeout { get; set; } = 60000;
}

public class FlareSolverrResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("solution")]
    public FlareSolverrSolution? Solution { get; set; }
}

public class FlareSolverrSolution
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("cookies")]
    public List<FlareSolverrCookie>? Cookies { get; set; }

    [JsonPropertyName("userAgent")]
    public string UserAgent { get; set; } = string.Empty;
}

public class FlareSolverrCookie
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}
