using FastEndpoints;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Scrapper.GetQueue;

public class ScraptMeta()
{
    public KomikuService KomikuService;
    public IMangaRepository Repository;
}
public class EventBus : IEventHandler<ScraptMeta>
{
    private readonly ILogger _logger;

    public EventBus(ILogger<ScraptMeta> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(ScraptMeta eventModel, CancellationToken ct)
    {
        var datas = await eventModel.Repository.GetAllAsync(ct);
        foreach (var item in datas)
        {
            await Task.Delay(2000);
            var updateMangaDocument = await eventModel.KomikuService.UpdateMangaDocument(item);
            await eventModel.Repository.UpdateAsync(updateMangaDocument, ct);
            _logger.LogInformation("Update manga {Title}", item.Title);
        }
    }
}