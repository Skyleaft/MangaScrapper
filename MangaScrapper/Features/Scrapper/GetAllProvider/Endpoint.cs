using System.Text.Json;
using FastEndpoints;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Features.Scrapper.GetAllProvider;

public class Endpoint(ScrapperService scrapperService) : EndpointWithoutRequest<List<Response>>
{
    public override void Configure()
    {
        Get("/api/scrapper/providers");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var providers = await scrapperService.GetAllProvider();
        var response = providers.Select(p => new Response
        {
            ProviderName = p.ProviderName,
            BaseUrl = p.BaseUrl
        }).ToList();
        await Send.OkAsync(response, ct);
    }
}
