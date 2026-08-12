using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.ValueObjects;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Domain.Repositories;

public interface IMangaRepository
{
    Task<Manga?> GetByIdAsync(MangaId id, CancellationToken ct = default);
    Task<Manga?> GetByTitleAsync(string title, CancellationToken ct = default);
    Task<PagedList<Manga>> GetPagedAsync(
        string? search, 
        List<string>? genres, 
        string? status, 
        string? type,
        string sortBy,
        string orderBy,
        int page, 
        int pageSize, 
        CancellationToken ct = default);
    Task<List<Manga>> GetByIdsAsync(List<Guid> ids, CancellationToken ct);
    Task AddAsync(Manga manga, CancellationToken ct = default);
    Task UpdateAsync(Manga manga, CancellationToken ct = default);
    Task DeleteAsync(MangaId id, CancellationToken ct = default);
    Task<List<string>> GetAllGenresAsync(CancellationToken ct);
    Task<List<string>> GetAllTypesAsync(CancellationToken ct);
    Task<DashboardStatistic> GetStatisticsAsync(CancellationToken ct);
    Task<(List<Manga> Items, long TotalCount)> GetTrendingAsync(
        string? search, 
        List<string>? genres, 
        string? status, 
        string? type,
        int page, 
        int pageSize, 
        CancellationToken ct);

    Task<List<Manga>> GetAllAsync(CancellationToken ct = default);
}

public interface IMangaExternalRepository
{
    Task<PagedList<Manga>> SearchAsync(
        string? search,
        List<string>? genres,
        string? status,
        string? type,
        string sortBy,
        string orderBy,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<List<Manga>> GetRecomendationAsync(List<Guid>readingHistoryIds,int limit,  CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(UserId id, CancellationToken ct = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<PagedList<User>> GetPagedAsync(
        string? search, 
        string sortBy,
        string orderBy,
        int page, 
        int pageSize, 
        CancellationToken ct = default);
    Task<User?> GetByFirebaseUidOrEmailAsync(string firebaseUid, string email, CancellationToken ct = default);
    Task<long> CountByRoleAsync(string role, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
}

public interface IUserLibraryRepository
{
    Task<UserLibrary?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserLibrary?> GetByUserIdAndMangaIdAsync(string userId, MangaId mangaId, CancellationToken ct = default);
    Task<PagedList<UserLibrary>> GetPagedByUserIdAsync(string userId,string? search,string? type,string? status,bool? isFavorite,string sortBy, string orderBy, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(UserLibrary userLibrary, CancellationToken ct = default);
    Task UpdateAsync(UserLibrary userLibrary, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IUserProgressionRepository
{
    Task<UserProgression?> GetByUserIdAndMangaIdAsync(string userId, MangaId mangaId, CancellationToken ct = default);
    Task<List<UserProgression>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task AddOrUpdateAsync(UserProgression userProgression, CancellationToken ct = default);
}
