using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.Dashboard;

// 1. GetStatistics
public record GetStatisticsQuery : IQuery<DashboardStatisticResponse>;

internal sealed class GetStatisticsQueryHandler(IMangaRepository mangaRepository)
    : IQueryHandler<GetStatisticsQuery, DashboardStatisticResponse>
{
    public async Task<Result<DashboardStatisticResponse>> Handle(GetStatisticsQuery query, CancellationToken ct)
    {
        var paged = await mangaRepository.GetPagedAsync(1, 1, ct: ct);
        var response = new DashboardStatisticResponse(
            TotalManga: paged.TotalCount,
            TotalSourceProvider: 4,
            ScrappedToday: 0,
            ScrappedThisMonth: 0,
            TotalQueue: 0,
            TotalUnlinkedMetadata: 0,
            TotalUnavailableMangaChapter: 0,
            TotalStorageUsed: 0,
            MonthlyScrap: []);

        return response;
    }
}

// 2. SyncStorage
public record SyncStorageCommand : ICommand<StorageSyncReportResponse>;

internal sealed class SyncStorageCommandHandler : ICommandHandler<SyncStorageCommand, StorageSyncReportResponse>
{
    public Task<Result<StorageSyncReportResponse>> Handle(SyncStorageCommand command, CancellationToken ct)
    {
        var report = new StorageSyncReportResponse(0, 0, 0, 0, []);
        return Task.FromResult<Result<StorageSyncReportResponse>>(report);
    }
}

public sealed class DashboardEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboard").WithTags("Dashboard");

        group.MapGet("/stats", async (ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetStatisticsQuery(), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetDashboardStats");

        group.MapPost("/sync-storage", async (ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SyncStorageCommand(), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("SyncStorage");
    }
}
