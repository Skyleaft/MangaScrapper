using MangaScrapper.Domain.Aggregates;
using Mapster;

namespace MangaScrapper.Infrastructure.Services;

public sealed class MangaInfrastructureMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Manga, MeiliMangaDocument>()
            .Map(dest => dest.Id, src => src.Id.Value.ToString())
            .Map(dest=>dest.ReleaseDate,src=>((DateTimeOffset)src.ReleaseDate.GetValueOrDefault().ToUniversalTime()).ToUnixTimeSeconds())
            .Map(dest => dest.CreatedAtTimestamp, src => ((DateTimeOffset)src.CreatedAt.ToUniversalTime()).ToUnixTimeSeconds())
            .Map(dest => dest.UpdatedAtTimestamp, src => ((DateTimeOffset)src.UpdatedAt.ToUniversalTime()).ToUnixTimeSeconds())
            .Map(dest => dest.TotalView, src=>src.Chapters.Sum(x=>x.TotalView))
            .Map(dest => dest.LatestChapter, src=>src.Chapters.OrderByDescending(c => c.Number).FirstOrDefault().Adapt<MeiliChapterDocument>());

        config.NewConfig<Chapter, MeiliChapterDocument>()
            .Map(dest => dest.Id, src => src.Id.Value.ToString())
            .Map(dest=>dest.UploadDateTimestamp,src=>((DateTimeOffset)src.UploadDate.ToUniversalTime()).ToUnixTimeSeconds());
    }
}