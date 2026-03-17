using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.UserProgression.Services;

public interface IUserProgressionService
{
    Task<UserProgressionDocument> UpdateProgressionAsync(Guid userId, Guid mangaId, Guid chapterId, double chapterNumber, int lastReadPage, int totalPages, int readingTimeSeconds, CancellationToken ct);
    Task<List<UserProgressionDocument>> GetUserProgressionsAsync(Guid userId, CancellationToken ct);
    Task<UserProgressionDocument?> GetMangaProgressionAsync(Guid userId, Guid mangaId, CancellationToken ct);
}
