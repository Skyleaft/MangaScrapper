using MangaScrapper.Application.Common.Abstractions;
using NovaStack.Contracts.Responses;

namespace MangaScrapper.Application.Features.Manga.GetPagedManga;

public record GetPagedMangaQuery(
    int Page = 1,
    int PageSize = 10,
    string? Search = null,
    string? Type = null,
    string? Genre = null) : IQuery<PagedResponse<MangaSummaryResponse>>;
