using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Common.Abstractions;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.ValueObjects;
using Mapster;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Core.Features.Mangas.UpdateManga;

public record UpdateMangaCommand(
    Guid Id,
    int MalId,
    int? AnilistId,
    long? MangaUpdateId,
    string Author,
    string Type,
    List<string>? Synonyms,
    List<string> Genres,
    List<string> Categories,
    string? Description,
    double? Rating,
    DateTime? ReleaseDate,
    bool? Nsfw,
    string? Status,
    int TotalView,
    int Popularity,
    int Members) : ICommand<MangaSummaryResponse>;

internal sealed class UpdateMangaCommandHandler(
    IMangaRepository mangaRepository,
    IMangaExternalRepository mangaExternalRepository)
    : ICommandHandler<UpdateMangaCommand, MangaSummaryResponse>
{
    public async Task<Result<MangaSummaryResponse>> Handle(UpdateMangaCommand command, CancellationToken ct)
    {
        var manga = await mangaRepository.GetByIdAsync(MangaId.From(command.Id), ct);

        if (manga is null)
            return Error.NotFound("Manga.NotFound", $"Manga with Id '{command.Id}' was not found.");

        manga.UpdateMetadata(
            command.MalId,
            command.AnilistId,
            command.MangaUpdateId,
            command.Author,
            command.Type,
            command.Synonyms,
            command.Genres,
            command.Categories,
            command.Description,
            command.Rating,
            command.Popularity,
            command.Members,
            command.Nsfw,
            command.Status,
            command.ReleaseDate,
            command.TotalView);

        await mangaRepository.UpdateAsync(manga, ct);

        // Update Meilisearch,Qdrant via external repository
        await mangaExternalRepository.IndexMangaAsync(manga, ct);
        await mangaExternalRepository.UpsertMangaAsync(manga, ct);

        return manga.Adapt<MangaSummaryResponse>();
    }
}

public sealed class UpdateMangaEndpoint : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/v1/manga/{id:guid}", HandleUpdateAsync)
            .WithName("UpdateManga")
            .RequireAuthorization(User.UserRoles.SuperUser)
            .WithSummary("Update manga details")
            .WithTags("Manga")
            .Produces<ApiResponse<object>>();
    }

    private static async Task<IResult> HandleUpdateAsync(
        Guid id,
        UpdateMangaCommand request,
        ISender sender,
        CancellationToken ct)
    {
        // Ensure the ID from the route matches the body
        if (id != request.Id)
        {
            request = request with { Id = id };
        }

        var result = await sender.Send(request, ct);
        return result.IsSuccess
            ? Results.Ok(ApiResponse.Ok<object?>(null, "Manga updated successfully"))
            : result.Error.ToHttpResult();
    }
}
