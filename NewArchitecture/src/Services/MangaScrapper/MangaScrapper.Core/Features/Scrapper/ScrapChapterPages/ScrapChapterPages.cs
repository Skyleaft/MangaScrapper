using MangaScrapper.Core.Common.Abstractions;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Core.Features.Scrapper.ScrapChapterPages;

public record ScrapChapterPagesCommand(Guid MangaId) : ICommand<int>;

internal sealed class ScrapChapterPagesCommandHandler(
    IMangaRepository mangaRepository,
    IScrapperQueueService queueService)
    : ICommandHandler<ScrapChapterPagesCommand, int>
{
    public async Task<Result<int>> Handle(ScrapChapterPagesCommand command, CancellationToken ct)
    {
        var manga = await mangaRepository.GetByIdAsync(new MangaId(command.MangaId), ct);
        if (manga is null)
            return Error.NotFound("Manga.NotFound", $"Manga with ID '{command.MangaId}' was not found.");

        var index = 0;
        foreach (var chapter in manga.Chapters.OrderBy(x => x.Number))
        {
            if (chapter.Pages.Count == 0)
            {
                await queueService.QueueChapterScraping(manga.Id.Value, manga.Title, chapter);
            }
            index++;
        }

        return index;
    }
}

public sealed class ScrapChapterPagesEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/scrapper").WithTags("Scrapper");

        group.MapGet("/manga/{mangaId:guid}/chapter-pages", async (Guid mangaId, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new ScrapChapterPagesCommand(mangaId), ct);
            return res.IsSuccess
                ? Results.Ok(ApiResponse.Ok(new { Message = $"Scraping {res.Value} jobs queued for missing chapters." }))
                : res.Error.ToHttpResult();
        }).WithName("ScrapChapterPages");
    }
}
