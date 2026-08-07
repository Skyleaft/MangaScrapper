namespace NovaStack.Contracts.Responses;

public record MangaSummaryResponse(
    Guid Id,
    int MalId,
    string Title,
    string Author,
    string Type,
    List<string>? Genres,
    string? Description,
    string? ImageUrl,
    string? LocalImageUrl,
    long ThumbnailSize,
    double? Rating,
    int Popularity,
    int Members,
    DateTime? ReleaseDate,
    string? Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? Url,
    int TotalView,
    LatestChapterSummaryResponse LatestChapter);

public record LatestChapterSummaryResponse(
    Guid Id,
    double Number,
    int TotalView,
    string? ChapterProvider,
    string? ChapterProviderIcon,
    string Language,
    DateTime UploadDate);

public record ChapterResponse(
    Guid Id,
    double Number,
    string? Title,
    string? Url,
    List<string> Pages,
    string Language,
    string? ChapterProvider,
    string? ChapterProviderIcon,
    DateTime UploadDate,
    int TotalView);

public record UserLibraryResponse(
    Guid Id,
    string UserId,
    Guid MangaId,
    DateTime AddedAt,
    MangaSummaryResponse? Manga);

public record UserProgressionResponse(
    Guid Id,
    string UserId,
    Guid MangaId,
    Guid LastReadChapterId,
    double LastReadChapterNumber,
    DateTime LastReadAt);
