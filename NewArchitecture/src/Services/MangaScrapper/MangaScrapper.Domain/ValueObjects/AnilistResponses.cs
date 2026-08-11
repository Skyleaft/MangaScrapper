using System.Text.Json.Serialization;

namespace MangaScrapper.Domain.ValueObjects;

public record AnilistResponse(
    [property: JsonPropertyName("data")] AnilistData? Data);

public record AnilistData(
    [property: JsonPropertyName("Page")] AnilistPage? Page);

public record AnilistPage(
    [property: JsonPropertyName("media")] List<AnilistMedia>? Media);

public record AnilistMedia(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("idMal")] int? IdMal,
    [property: JsonPropertyName("title")] AnilistTitle? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("format")] string? Format,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("chapters")] int? Chapters,
    [property: JsonPropertyName("volumes")] int? Volumes,
    [property: JsonPropertyName("coverImage")] AnilistCoverImage? CoverImage,
    [property: JsonPropertyName("averageScore")] int? AverageScore,
    [property: JsonPropertyName("popularity")] int? Popularity,
    [property: JsonPropertyName("genres")] List<string>? Genres,
    [property: JsonPropertyName("startDate")] AnilistFuzzyDate? StartDate);

public record AnilistTitle(
    [property: JsonPropertyName("romaji")] string? Romaji,
    [property: JsonPropertyName("english")] string? English,
    [property: JsonPropertyName("native")] string? Native);

public record AnilistCoverImage(
    [property: JsonPropertyName("extraLarge")] string? ExtraLarge,
    [property: JsonPropertyName("large")] string? Large,
    [property: JsonPropertyName("medium")] string? Medium);

public record AnilistFuzzyDate(
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("month")] int? Month,
    [property: JsonPropertyName("day")] int? Day);
