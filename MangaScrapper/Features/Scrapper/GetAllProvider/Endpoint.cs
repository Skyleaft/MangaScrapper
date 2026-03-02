using System.Text.Json;
using FastEndpoints;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Features.Scrapper.GetAllProvider;

public class Endpoint(IMangaRepository repo, IOptions<ScrapperSettings> settings) : EndpointWithoutRequest<List<Response>>
{
    public override void Configure()
    {
        Get("/api/scrapper/providers");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var providers = new List<ScrapperProvider>();
        var providerFolder = Path.Combine(Directory.GetCurrentDirectory(), "provider");
        
        if (!Directory.Exists(providerFolder))
        {
            await Send.OkAsync(new List<Response>(), ct);
            return;
        }

        var jsonFiles = Directory.GetFiles(providerFolder, "*.json");
        
        foreach (var file in jsonFiles)
        {
            try
            {
                var jsonContent = await File.ReadAllTextAsync(file, ct);
                var provider = JsonSerializer.Deserialize<ScrapperProvider>(jsonContent);
                
                if (provider != null)
                {
                    providers.Add(provider);
                }
            }
            catch (Exception)
            {
                // Skip invalid JSON files
            }
        }
        
        var response = providers.Select(p => new Response
        {
            ProviderName = p.ProviderName,
            BaseUrl = p.BaseUrl
        }).ToList();
        
        await Send.OkAsync(response, ct);
    }
}
