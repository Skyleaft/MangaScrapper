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
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("UserId must be a valid GUID.");
        RuleFor(x => x.MangaId).NotEmpty().WithMessage("MangaId is required.");
        RuleFor(x => x.ChapterId).NotEmpty().WithMessage("ChapterId is required.");
        RuleFor(x => x.LastReadPage).GreaterThanOrEqualTo(0).WithMessage("LastReadPage must be >= 0.");
        RuleFor(x => x.TotalPages).GreaterThanOrEqualTo(0).WithMessage("TotalPages must be >= 0.");
        RuleFor(x => x.ReadingTimeSeconds).GreaterThanOrEqualTo(0).WithMessage("ReadingTimeSeconds must be >= 0.");
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

        var progression = await progressionRepository.UpsertChapterLogAsync(command.UserId, mangaId, chapterLog, ct);

        // Non-blocking SignalR broadcast with CancellationToken.None so client HTTP cancellations don't abort notification
        _ = BroadcastProgressionUpdatedAsync(command, progression, chapterLog);

        return ToResponse(progression, command.ChapterId);
    }

    private async Task BroadcastProgressionUpdatedAsync(
        UpdateUserProgressionCommand command,
        Aggregates.UserProgression progression,
        Aggregates.UserProgression.ChapterLog chapterLog)
    {
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

            await mangaHubContext.Clients.Group(userGroup).SendAsync("UserProgressionUpdated", payload, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast UserProgressionUpdated for User={UserId}, Manga={MangaId}", command.UserId, command.MangaId);
        }
    }

    private static UserProgressionResponse ToResponse(Aggregates.UserProgression p, Guid targetChapterId)
    {
        var targetLog = p.ChapterLogs.FirstOrDefault(cl => cl.ChapterId == targetChapterId);
        var logs = targetLog != null
            ? new List<ChapterLogsResponse>(1)
            {
                new(targetLog.Id, targetLog.ChapterId, targetLog.ChapterNumber, targetLog.LastReadPage,
                    targetLog.TotalPages, targetLog.IsCompleted, targetLog.ReadingTimeSeconds, targetLog.LastReadAt)
            }
            : new List<ChapterLogsResponse>(0);

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
