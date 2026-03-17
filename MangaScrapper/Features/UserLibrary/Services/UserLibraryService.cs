using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.UserLibrary.Services;

public class UserLibraryService : IUserLibraryService
{
    private readonly IUserLibraryRepository _repository;

    public UserLibraryService(IUserLibraryRepository repository)
    {
        _repository = repository;
    }

    public async Task<UserLibraryDocument> AddOrUpdateLibraryEntryAsync(Guid userId, Guid mangaId, string mangaTitle, string mangaImageUrl, string status, CancellationToken ct)
    {
        var existing = await _repository.GetByUserAndMangaAsync(userId, mangaId, ct);
        if (existing != null)
        {
            existing.Status = status;
            await _repository.UpdateAsync(existing, ct);
            return existing;
        }

        var newEntry = new UserLibraryDocument
        {
            UserId = userId,
            MangaId = mangaId,
            MangaTitle = mangaTitle,
            MangaImageUrl = mangaImageUrl,
            Status = status
        };

        return await _repository.CreateAsync(newEntry, ct);
    }

    public async Task<List<UserLibraryDocument>> GetUserLibraryAsync(Guid userId, CancellationToken ct)
    {
        return await _repository.GetByUserAsync(userId, ct);
    }

    public async Task RemoveFromLibraryAsync(Guid userId, Guid mangaId, CancellationToken ct)
    {
        var existing = await _repository.GetByUserAndMangaAsync(userId, mangaId, ct);
        if (existing != null)
        {
            await _repository.DeleteAsync(existing.Id, ct);
        }
    }
}
