using System;
using MangaScrapper.Application.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.RecurringJobs;

public record RecurringJobDto(string Id, string Cron, string Queue, DateTime? NextExecution, DateTime? LastExecution, string LastJobState, DateTime? CreatedAt);

public record GetRecurringJobsQuery : IQuery<List<RecurringJobDto>>;

public record CreateOrUpdateRecurringJobCommand(string JobId, string CronExpression, string Provider, int ScrapLastTotalPage) : ICommand;

public record DeleteRecurringJobCommand(string JobId) : ICommand;

public record TriggerRecurringJobCommand(string JobId) : ICommand;

internal sealed class RecurringJobsQueryHandler(IRecurringJobsService recurringJobsService) : IQueryHandler<GetRecurringJobsQuery, List<RecurringJobDto>>
{
    public async Task<Result<List<RecurringJobDto>>> Handle(GetRecurringJobsQuery query, CancellationToken ct)
    {
        var jobs = await recurringJobsService.GetRecurringJobsAsync(ct);
        return Result.Success(jobs);
    }
}

internal sealed class CreateOrUpdateRecurringJobCommandHandler(IRecurringJobsService recurringJobsService) : ICommandHandler<CreateOrUpdateRecurringJobCommand>
{
    public async Task<Result> Handle(CreateOrUpdateRecurringJobCommand command, CancellationToken ct)
    {
        await recurringJobsService.CreateOrUpdateLatestChapterScrapingJobAsync(command.JobId, command.CronExpression, command.Provider, command.ScrapLastTotalPage, ct);
        return Result.Success();
    }
}

internal sealed class DeleteRecurringJobCommandHandler(IRecurringJobsService recurringJobsService) : ICommandHandler<DeleteRecurringJobCommand>
{
    public async Task<Result> Handle(DeleteRecurringJobCommand command, CancellationToken ct)
    {
        await recurringJobsService.DeleteRecurringJobAsync(command.JobId, ct);
        return Result.Success();
    }
}

internal sealed class TriggerRecurringJobCommandHandler(IRecurringJobsService recurringJobsService) : ICommandHandler<TriggerRecurringJobCommand>
{
    public async Task<Result> Handle(TriggerRecurringJobCommand command, CancellationToken ct)
    {
        await recurringJobsService.TriggerRecurringJobAsync(command.JobId, ct);
        return Result.Success();
    }
}

public sealed class RecurringJobsEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/recurring-jobs").WithTags("RecurringJobs");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetRecurringJobsQuery(), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetRecurringJobs");

        group.MapPost("/", async (CreateOrUpdateRecurringJobCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(cmd, ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "Recurring job updated")) : res.Error.ToHttpResult();
        }).WithName("CreateOrUpdateRecurringJob");

        group.MapDelete("/{jobId}", async (string jobId, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new DeleteRecurringJobCommand(jobId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "Recurring job deleted")) : res.Error.ToHttpResult();
        }).WithName("DeleteRecurringJob");

        group.MapPost("/{jobId}/trigger", async (string jobId, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new TriggerRecurringJobCommand(jobId), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "Recurring job triggered")) : res.Error.ToHttpResult();
        }).WithName("TriggerRecurringJob");
    }
}
