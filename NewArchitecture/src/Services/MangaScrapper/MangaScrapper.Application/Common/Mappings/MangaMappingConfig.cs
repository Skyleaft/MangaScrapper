using MangaScrapper.Domain.Aggregates;
using Mapster;
using NovaStack.Contracts.Responses;

namespace MangaScrapper.Application.Common.Mappings;

public sealed class MangaMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Manga, MangaSummaryResponse>()
            .Map(dest => dest.Id, src => src.Id.Value)
            .Map(dest => dest.LatestChapter, src => src.Chapters.OrderByDescending(c => c.Number).FirstOrDefault().Adapt<ChapterResponse>());

        config.NewConfig<Chapter, ChapterResponse>()
            .Map(dest => dest.Id, src => src.Id.Value)
            .Map(dest => dest.Pages, src => src.Pages.Select(x => x.LocalImageUrl).ToList());
    }
}
