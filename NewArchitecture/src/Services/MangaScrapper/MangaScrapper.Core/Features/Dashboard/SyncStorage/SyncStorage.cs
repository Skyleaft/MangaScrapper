using MangaScrapper.Core.Common.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Core.Features.Dashboard.SyncStorage;

public record SyncStorageCommand : ICommand<StorageSyncReportResponse>;

internal sealed class SyncStorageCommandHandler : ICommandHandler<SyncStorageCommand, StorageSyncReportResponse>
{
    public Task<Result<StorageSyncReportResponse>> Handle(SyncStorageCommand command, CancellationToken ct)
    {
        var report = new StorageSyncReportResponse(0, 0, 0, 0, []);
        return Task.FromResult<Result<StorageSyncReportResponse>>(report);
    }
}

public sealed class SyncStorageEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboard").WithTags("Dashboard");

        group.MapPost("/sync-storage", async (ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SyncStorageCommand(), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("SyncStorage");
    }
}
