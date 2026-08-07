using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.ValueObjects;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Domain.Repositories;

public interface IMangaRepository
{
    Task<Manga?> GetByIdAsync(MangaId id, CancellationToken ct = default);
    Task<Manga?> GetByTitleAsync(string title, CancellationToken ct = default);
    Task<PagedList<Manga>> GetPagedAsync(int page, int pageSize, string? search = null, string? type = null, string? genre = null, CancellationToken ct = default);
    Task AddAsync(Manga manga, CancellationToken ct = default);
    Task UpdateAsync(Manga manga, CancellationToken ct = default);
    Task DeleteAsync(MangaId id, CancellationToken ct = default);
}

public interface IUserLibraryRepository
{
    Task<UserLibrary?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserLibrary?> GetByUserIdAndMangaIdAsync(string userId, MangaId mangaId, CancellationToken ct = default);
    Task<PagedList<UserLibrary>> GetPagedByUserIdAsync(string userId, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(UserLibrary userLibrary, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IUserProgressionRepository
{
    Task<UserProgression?> GetByUserIdAndMangaIdAsync(string userId, MangaId mangaId, CancellationToken ct = default);
    Task<List<UserProgression>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task AddOrUpdateAsync(UserProgression userProgression, CancellationToken ct = default);
}
