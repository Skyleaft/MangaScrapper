using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.UserProgression.Services;

public class UserProgressionService : IUserProgressionService
{
    private readonly IUserProgressionRepository _repository;

    public UserProgressionService(IUserProgressionRepository repository)
    {
        _repository = repository;
    }

    public async Task<UserProgressionDocument> UpdateProgressionAsync(Guid userId, Guid mangaId, Guid chapterId, double chapterNumber, int lastReadPage, int totalPages, int readingTimeSeconds, CancellationToken ct)
    {
        var existing = await _repository.GetByUserAndMangaAsync(userId, mangaId, ct);
        var isCompleted = lastReadPage >= totalPages - 1 && totalPages > 0;

        if (existing != null)
        {
            if (existing.ChapterLogs == null)
            {
                existing.ChapterLogs = new List<UserChapterLogDocument>();
            }

            var log = existing.ChapterLogs.FirstOrDefault(x => x.ChapterId == chapterId);
            if (log != null)
            {
                log.ChapterNumber = chapterNumber;
                log.LastReadPage = lastReadPage;
                log.TotalPages = totalPages;
                log.ReadingTimeSeconds += readingTimeSeconds;
                log.IsCompleted = isCompleted;
                log.LastReadAt = DateTime.UtcNow;
            }
            else
            {
                existing.ChapterLogs.Add(new UserChapterLogDocument
                {
                    ChapterId = chapterId,
                    ChapterNumber = chapterNumber,
                    LastReadPage = lastReadPage,
                    TotalPages = totalPages,
                    ReadingTimeSeconds = readingTimeSeconds,
                    IsCompleted = isCompleted,
                    LastReadAt = DateTime.UtcNow
                });
            }

            existing.TotalReadingTime += readingTimeSeconds;
            existing.LastReadAt = DateTime.UtcNow;

            await _repository.UpdateAsync(existing, ct);
            return existing;
        }

        var newEntry = new UserProgressionDocument
        {
            UserId = userId,
            MangaId = mangaId,
            TotalReadingTime = readingTimeSeconds,
            LastReadAt = DateTime.UtcNow,
            ChapterLogs = new List<UserChapterLogDocument>
            {
                new UserChapterLogDocument
                {
                    ChapterId = chapterId,
                    ChapterNumber = chapterNumber,
                    LastReadPage = lastReadPage,
                    TotalPages = totalPages,
                    ReadingTimeSeconds = readingTimeSeconds,
                    IsCompleted = isCompleted,
                    LastReadAt = DateTime.UtcNow
                }
            }
        };

        return await _repository.CreateAsync(newEntry, ct);
    }

    public async Task<List<UserProgressionDocument>> GetUserProgressionsAsync(Guid userId, CancellationToken ct)
    {
        return await _repository.GetByUserAsync(userId, ct);
    }

    public async Task<UserProgressionDocument?> GetMangaProgressionAsync(Guid userId, Guid mangaId, CancellationToken ct)
    {
        return await _repository.GetByUserAndMangaAsync(userId, mangaId, ct);
    }
}
