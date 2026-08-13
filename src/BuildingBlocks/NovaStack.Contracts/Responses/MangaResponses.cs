namespace NovaStack.Contracts.Responses;

public record MangaSummaryResponse(
    Guid Id,
    int MalId,
    int? AnilistId,
    long? MangaUpdateId,
    string Title,
    string Author,
    string Type,
    List<string>? Genres,
    List<string>? Categories,
    string? Description,
    string? ImageUrl,
    string? LocalImageUrl,
    long ThumbnailSize,
    double? Rating,
    int Popularity,
    int Members,
    DateTime? ReleaseDate,
    bool? Nsfw,
    string? Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? Url,
    int TotalView,
    ChapterResponse LatestChapter);

public record ChapterResponse(
    Guid Id,
    double Number,
    string? Link,
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
    DateTime UpdatedAt,
    string? Status,
    bool? IsFavorite,
    MangaSummaryResponse? Manga);

public record UserProgressionResponse(
    Guid Id,
    string UserId,
    Guid MangaId,
    DateTime LastReadAt,
    int TotalReadingTime,
    List<ChapterLogsResponse> ChapterLogs
    );

public record ChapterLogsResponse(
    Guid Id,
    Guid ChapterId,
    double ChapterNumber,
    int LastReadPage,
    int TotalPage,
    bool IsCompleted,
    int ReadTimeInSeconds,
    DateTime? LastReadAt);