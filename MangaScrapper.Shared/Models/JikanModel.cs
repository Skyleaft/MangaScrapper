using System.Text.Json.Serialization;

namespace MangaScrapper.Shared.Models;

public class JikanMangaSearchDto
{
    public int MalId { get; set; }
    public string? Title { get; set; }
    public string? Thumbnail { get; set; }
    public double? Score { get; set; }
}

public class JikanMangaResponse
{
    [JsonPropertyName("data")]
    public List<JikanMangaItem>? Data { get; set; }

    [JsonPropertyName("pagination")]
    public JikanPagination? Pagination { get; set; }
}

public class JikanMangaSingleResponse
{
    [JsonPropertyName("data")]
    public JikanMangaItem? Data { get; set; }
}

public class JikanMangaItem
{
    [JsonPropertyName("mal_id")]
    public int MalId { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("title_english")]
    public string? TitleEnglish { get; set; }

    [JsonPropertyName("title_japanese")]
    public string? TitleJapanese { get; set; }
    
    [JsonPropertyName("title_synonyms")]
    public List<string>? TitleSynonyms { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("chapters")]
    public int? Chapters { get; set; }

    [JsonPropertyName("volumes")]
    public int? Volumes { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("synopsis")]
    public string? Synopsis { get; set; }

    [JsonPropertyName("images")]
    public JikanImages? Images { get; set; }

    [JsonPropertyName("authors")] 
    public List<JikanAuthor> Authors { get; set; } = new List<JikanAuthor>();

    [JsonPropertyName("genres")]
    public List<JikanGenre>? Genres { get; set; }

    [JsonPropertyName("published")]
    public JikanPublished? Published { get; set; }
    
    [JsonPropertyName("popularity")]
    public int Popularity { get; set; }
    
    [JsonPropertyName("members")]
    public int Members { get; set; }
}

public class JikanImages
{
    [JsonPropertyName("jpg")]
    public JikanImageDetail? Jpg { get; set; }

    [JsonPropertyName("webp")]
    public JikanImageDetail? Webp { get; set; }
}

public class JikanImageDetail
{
    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("small_image_url")]
    public string? SmallImageUrl { get; set; }

    [JsonPropertyName("large_image_url")]
    public string? LargeImageUrl { get; set; }
}

public class JikanGenre
{
    [JsonPropertyName("mal_id")]
    public int MalId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class JikanAuthor
{
    
    [JsonPropertyName("mal_id")]
    public int MalId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class JikanPublished
{
    [JsonPropertyName("from")]
    public DateTime? From { get; set; }

    [JsonPropertyName("to")]
    public DateTime? To { get; set; }

    [JsonPropertyName("string")]
    public string? String { get; set; }
}

public class JikanPagination
{
    [JsonPropertyName("last_visible_page")]
    public int LastVisiblePage { get; set; }

    [JsonPropertyName("has_next_page")]
    public bool HasNextPage { get; set; }

    [JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }
}
