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
        if (existing != null)
        {
            existing.ChapterId = chapterId;
            existing.ChapterNumber = chapterNumber;
            existing.LastReadPage = lastReadPage;
            existing.TotalPages = totalPages;
            existing.ReadingTimeSeconds += readingTimeSeconds;
            
            if (lastReadPage >= totalPages - 1 && totalPages > 0)
            {
                existing.IsCompleted = true;
            }

            await _repository.UpdateAsync(existing, ct);
            return existing;
        }

        var newEntry = new UserProgressionDocument
        {
            UserId = userId,
            MangaId = mangaId,
            ChapterId = chapterId,
            ChapterNumber = chapterNumber,
            LastReadPage = lastReadPage,
            TotalPages = totalPages,
            ReadingTimeSeconds = readingTimeSeconds,
            IsCompleted = lastReadPage >= totalPages - 1 && totalPages > 0
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
