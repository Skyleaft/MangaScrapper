using FluentValidation;
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.UserProgression;

// 1. UpdateUserProgression
public record UpdateUserProgressionCommand(
    string UserId,
    Guid MangaId,
    Guid LastReadChapterId,
    double LastReadChapterNumber) : ICommand<UserProgressionResponse>;

public class UpdateUserProgressionCommandValidator : AbstractValidator<UpdateUserProgressionCommand>
{
    public UpdateUserProgressionCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.MangaId).NotEmpty().WithMessage("MangaId is required.");
        RuleFor(x => x.LastReadChapterId).NotEmpty().WithMessage("LastReadChapterId is required.");
    }
}

internal sealed class UpdateUserProgressionCommandHandler(IUserProgressionRepository progressionRepository)
    : ICommandHandler<UpdateUserProgressionCommand, UserProgressionResponse>
{
    public async Task<Result<UserProgressionResponse>> Handle(UpdateUserProgressionCommand command, CancellationToken ct)
    {
        var mangaId = MangaId.From(command.MangaId);
        var chapterId = ChapterId.From(command.LastReadChapterId);

        var existing = await progressionRepository.GetByUserIdAndMangaIdAsync(command.UserId, mangaId, ct);
        if (existing is not null)
        {
            existing.UpdateProgression(chapterId, command.LastReadChapterNumber);
            await progressionRepository.AddOrUpdateAsync(existing, ct);
            return new UserProgressionResponse(existing.Id, existing.UserId, existing.MangaId.Value, existing.LastReadChapterId.Value, existing.LastReadChapterNumber, existing.LastReadAt);
        }

        var progression = Domain.Aggregates.UserProgression.Create(command.UserId, mangaId, chapterId, command.LastReadChapterNumber);
        await progressionRepository.AddOrUpdateAsync(progression, ct);

        return new UserProgressionResponse(progression.Id, progression.UserId, progression.MangaId.Value, progression.LastReadChapterId.Value, progression.LastReadChapterNumber, progression.LastReadAt);
    }
}

// 2. GetUserProgression
public record GetUserProgressionQuery(string UserId) : IQuery<List<UserProgressionResponse>>;

internal sealed class GetUserProgressionQueryHandler(IUserProgressionRepository progressionRepository)
    : IQueryHandler<GetUserProgressionQuery, List<UserProgressionResponse>>
{
    public async Task<Result<List<UserProgressionResponse>>> Handle(GetUserProgressionQuery query, CancellationToken ct)
    {
        var items = await progressionRepository.GetByUserIdAsync(query.UserId, ct);
        var mapped = items.Select(p => new UserProgressionResponse(p.Id, p.UserId, p.MangaId.Value, p.LastReadChapterId.Value, p.LastReadChapterNumber, p.LastReadAt)).ToList();
        return mapped;
    }
}

// 3. GetMangaProgression
public record GetMangaProgressionQuery(string UserId, Guid MangaId) : IQuery<UserProgressionResponse>;

internal sealed class GetMangaProgressionQueryHandler(IUserProgressionRepository progressionRepository)
    : IQueryHandler<GetMangaProgressionQuery, UserProgressionResponse>
{
    public async Task<Result<UserProgressionResponse>> Handle(GetMangaProgressionQuery query, CancellationToken ct)
    {
        var p = await progressionRepository.GetByUserIdAndMangaIdAsync(query.UserId, MangaId.From(query.MangaId), ct);
        if (p is null)
            return Error.NotFound("UserProgression.NotFound", "No progression recorded for this manga.");

        return new UserProgressionResponse(p.Id, p.UserId, p.MangaId.Value, p.LastReadChapterId.Value, p.LastReadChapterNumber, p.LastReadAt);
    }
}

// Endpoints
public sealed class UserProgressionEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/user-progression").WithTags("UserProgression");

        group.MapPost("/", async (UpdateUserProgressionCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(cmd, ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("UpdateUserProgression");

        group.MapGet("/{userId}", async (string userId, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetUserProgressionQuery(userId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetUserProgression");

        group.MapGet("/{userId}/{mangaId:guid}", async (string userId, Guid mangaId, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetMangaProgressionQuery(userId, mangaId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetMangaProgression");
    }
}
