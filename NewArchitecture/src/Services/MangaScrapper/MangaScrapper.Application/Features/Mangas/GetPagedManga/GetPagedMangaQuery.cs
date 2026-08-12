using MangaScrapper.Application.Common.Abstractions;
using NovaStack.Contracts.Responses;

namespace MangaScrapper.Application.Features.Mangas.GetPagedManga;

public record GetPagedMangaQuery(
    string? Search = null,
    List<string>?Genres = null,
    string? Status = null,
    string? Type = null,
    int Page = 1,
    int PageSize = 10, 
    string? SortBy = "updatedAt",
    string? OrderBy = "desc") : IQuery<PagedResponse<MangaSummaryResponse>>;
