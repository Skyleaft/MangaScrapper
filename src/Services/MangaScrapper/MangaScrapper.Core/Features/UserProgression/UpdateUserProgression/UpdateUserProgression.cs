using FluentValidation;
using MangaScrapper.Core.Common.Abstractions;
using MangaScrapper.Core.Hubs;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Core.Features.UserProgression.UpdateUserProgression;

public record UpdateUserProgressionCommand(
    string UserId,
    Guid MangaId,
    Guid ChapterId,
    double ChapterNumber,
    int LastReadPage,
    int TotalPages,
    bool IsCompleted,
    int ReadingTimeSeconds,
    DateTime? LastReadAt = null,
    string? SenderDeviceId = null) : ICommand<UserProgressionResponse>;

public class UpdateUserProgressionCommandValidator : AbstractValidator<UpdateUserProgressionCommand>
{
    public UpdateUserProgressionCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.MangaId).NotEmpty().WithMessage("MangaId is required.");
        RuleFor(x => x.ChapterId).NotEmpty().WithMessage("ChapterId is required.");
    }
}

internal sealed class UpdateUserProgressionCommandHandler(
    IUserProgressionRepository progressionRepository,
    IHubContext<MangaHub> mangaHubContext,
    ILogger<UpdateUserProgressionCommandHandler> logger)
    : ICommandHandler<UpdateUserProgressionCommand, UserProgressionResponse>
{
    public async Task<Result<UserProgressionResponse>> Handle(UpdateUserProgressionCommand command, CancellationToken ct)
    {
        var mangaId = MangaId.From(command.MangaId);
        var lastReadAt = command.LastReadAt ?? DateTime.UtcNow;
        var chapterLog = Aggregates.UserProgression.ChapterLog.Create(
            command.ChapterId, command.ChapterNumber, command.LastReadPage, 
            command.TotalPages, command.IsCompleted, command.ReadingTimeSeconds,
            lastReadAt);

        var progression = await progressionRepository.GetByUserIdAndMangaIdAsync(command.UserId, mangaId, ct);
        if (progression is not null)
        {
            progression.UpdateProgression(chapterLog);
        }
        else
        {
            progression = Aggregates.UserProgression.Create(
                command.UserId, mangaId, chapterLog.ReadingTimeSeconds, [chapterLog], lastReadAt);
        }

        await progressionRepository.AddOrUpdateAsync(progression, ct);

        try
        {
            var userGroup = MangaHub.GetUserGroupName(command.UserId);
            var payload = new UserProgressionUpdatedPayload(
                command.UserId,
                command.MangaId,
                command.ChapterId,
                command.ChapterNumber,
                command.LastReadPage,
                command.TotalPages,
                command.IsCompleted,
                progression.LastReadAt,
                chapterLog.ReadingTimeSeconds,
                command.SenderDeviceId
            );

            await mangaHubContext.Clients.Group(userGroup).SendAsync("UserProgressionUpdated", payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast UserProgressionUpdated for User={UserId}, Manga={MangaId}", command.UserId, command.MangaId);
        }

        return ToResponse(progression);
    }

    private static UserProgressionResponse ToResponse(Aggregates.UserProgression p)
    {
        var logs = new List<ChapterLogsResponse>(p.ChapterLogs.Count);
        foreach (var cl in p.ChapterLogs)
        {
            logs.Add(new ChapterLogsResponse(
                cl.Id, cl.ChapterId, cl.ChapterNumber, cl.LastReadPage,
                cl.TotalPages, cl.IsCompleted, cl.ReadingTimeSeconds, cl.LastReadAt));
        }

        return new UserProgressionResponse(
            p.Id, p.UserId, p.MangaId.Value, p.LastReadAt, p.TotalReadingTime, logs);
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
