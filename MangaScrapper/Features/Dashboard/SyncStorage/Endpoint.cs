using FastEndpoints;
using MangaScrapper.Infrastructure.Services;
using MangaScrapper.Shared.Models;

namespace MangaScrapper.Features.Dashboard.SyncStorage;

public class Endpoint(StorageSyncService storageSyncService) : EndpointWithoutRequest<StorageSyncReport>
{
    public override void Configure()
    {
        Post("/api/dashboard/storage/sync");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var report = await storageSyncService.SyncStorageUsageAsync(ct);
        await Send.OkAsync(report, ct);
    }
}
