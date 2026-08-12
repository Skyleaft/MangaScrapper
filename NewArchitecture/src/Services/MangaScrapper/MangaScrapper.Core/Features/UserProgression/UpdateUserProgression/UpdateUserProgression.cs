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

namespace MangaScrapper.Application.Features.UserProgression.UpdateUserProgression;

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

public sealed class UpdateUserProgressionEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/user-progression").WithTags("UserProgression");

        group.MapPost("/", async (UpdateUserProgressionCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(cmd, ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("UpdateUserProgression")
        .Produces<ApiResponse<UserProgressionResponse>>();
    }
}
