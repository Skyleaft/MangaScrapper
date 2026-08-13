using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Common.Abstractions;
using NovaStack.Contracts.Responses;
using System.Net.Http.Json;
using System.Web;

namespace MangaScrapper.Core.Services;

public sealed class ExternalMetadataService : IExternalMetadataService
{
    private readonly HttpClient _httpClient;

    public ExternalMetadataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<Manga>> SearchJikanAsync(string title, CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["q"] = title;
        query["limit"] = "10";
        try
        {
            var response = await _httpClient.GetFromJsonAsync<JikanMangaResponse>($"https://api.jikan.moe/v4/manga?{query}", ct);
            var items = response?.Data ?? [];
            
            var result = new List<Manga>();
            foreach (var item in items)
            {
                var author = item.Authors?.FirstOrDefault()?.Name ?? "Unknown";
                var manga = Manga.Create(
                    title: item.Title ?? "Unknown",
                    author: author,
                    type: item.Type ?? "Unknown",
                    source: "Jikan",
                    malId: item.MalId,
                    genres: item.Genres?.Select(g => g.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? [],
                    description: item.Synopsis,
                    imageUrl: item.Images?.Jpg?.ImageUrl ?? item.Images?.Webp?.ImageUrl,
                    url: item.Url,
                    rating: item.Score,
                    status: item.Status
                );
                // Popularity, Members, ReleaseDate are missing in Create, we can use UpdateFromScrapper
                manga.UpdateFromScrapper(
                    malId: item.MalId,
                    rating: item.Score,
                    popularity: item.Popularity,
                    members: item.Members,
                    releaseDate: item.Published?.From,
                    status: item.Status,
                    author: author);
                result.Add(manga);
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    public async Task<List<Manga>> SearchAnilistAsync(string title, CancellationToken ct = default)
    {
        var url = "https://graphql.anilist.co";
        var query = new
        {
            query = @"
                query ($search: String) {
                    Page (page: 1, perPage: 10) {
                        media (search: $search, type: MANGA) {
                            id
                            idMal
                            title { romaji english native }
                            description
                            countryOfOrigin
                            format
                            status
                            chapters
                            volumes
                            coverImage { extraLarge large medium }
                            averageScore
                            popularity
                            genres
                            startDate { year month day }
                            staff {
                                edges {
                                    role
                                    node {
                                        name { full }
                                    }
                                }
                            }
                        }
                    }
                }",
            variables = new { search = title }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, query, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<AnilistResponse>(cancellationToken: ct);
            var items = result?.Data?.Page?.Media ?? [];
            
            var mangaList = new List<Manga>();
            foreach (var item in items)
            {
                var titleToUse = item.Title?.Romaji ?? item.Title?.English ?? item.Title?.Native ?? "Unknown";
                
                var author = "Unknown";
                var staffEdges = item.Staff?.Edges;
                if (staffEdges != null && staffEdges.Any())
                {
                    var mainAuthor = staffEdges.FirstOrDefault(e => e.Role != null && (e.Role.Contains("Story") || e.Role.Contains("Art") || e.Role.Contains("Original Creator")));
                    if (mainAuthor != null && mainAuthor.Node?.Name?.Full != null)
                    {
                        author = mainAuthor.Node.Name.Full;
                    }
                    else if (staffEdges.First().Node?.Name?.Full != null)
                    {
                        author = staffEdges.First().Node!.Name!.Full!;
                    }
                }

                var manga = Manga.Create(
                    title: titleToUse,
                    author: author,
                    type: item.ComicType.ToString(),
                    source: "Anilist",
                    malId: item.IdMal ?? 0,
                    anilistId: item.Id,
                    genres: item.Genres ?? [],
                    description: item.Description,
                    imageUrl: item.CoverImage?.Large ?? item.CoverImage?.Medium ?? item.CoverImage?.ExtraLarge,
                    rating: item.AverageScore.HasValue ? item.AverageScore.Value / 10.0 : null,
                    status: item.Status
                );
                manga.UpdateFromAnilist(item);
                mangaList.Add(manga);
            }
            return mangaList;
        }
        catch
        {
            return [];
        }
    }

    public async Task<JikanMangaItem?> GetJikanMangaInfoAsync(string title, string type, CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["q"] = title;
        query["limit"] = "10";
        try
        {
            var response = await _httpClient.GetFromJsonAsync<JikanMangaResponse>($"https://api.jikan.moe/v4/manga?{query}", ct);
            var results = response?.Data ?? [];
            return results.FirstOrDefault(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase)) ?? results.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public async Task<JikanMangaItem?> GetJikanMangaInfoByIdAsync(int malId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<JikanMangaSingleResponse>($"https://api.jikan.moe/v4/manga/{malId}", ct);
            return response?.Data;
        }
        catch
        {
            return null;
        }
    }
}
