using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.ValueObjects;
using Mapster;
using NovaStack.Contracts.Responses;

namespace MangaScrapper.Application.Common.Mappings;

public sealed class MangaMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Manga, MangaSummaryResponse>()
            .Map(dest => dest.Id, src => src.Id.Value)
            .Map(dest=>dest.TotalView, src=>src.Chapters.Sum(x=>x.TotalView))
            .Map(dest => dest.LatestChapter, src => src.Chapters.OrderByDescending(c => c.Number).FirstOrDefault().Adapt<ChapterResponse>());

        config.NewConfig<Chapter, ChapterResponse>()
            .Map(dest => dest.Id, src => src.Id.Value)
            .Map(dest => dest.Pages, src => src.Pages.Select(x => x.LocalImageUrl).ToList());
        
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

        config.NewConfig<MangaDocument, Manga>()
            .ConstructUsing(doc => Manga.Reconstitute(
                MangaId.From(doc.Id),
                doc.Title,
                doc.Author,
                doc.Type,
                doc.MalID,
                doc.AnilistID,
                doc.Genres,
                doc.Description,
                doc.ImageUrl,
                doc.LocalImageUrl,
                doc.ThumbnailSize,
                doc.Rating,
                doc.Popularity,
                doc.Members,
                doc.Status,
                doc.ReleaseDate,
                doc.TotalView,
                doc.CreatedAt,
                doc.UpdatedAt,
                doc.Url,
                doc.Chapters != null ? doc.Chapters.Select(c => new Chapter(
                    ChapterId.From(c.Id),
                    c.Number,
                    c.Link,
                    c.ChapterProvider,
                    c.ChapterProviderIcon,
                    c.Language,
                    c.TotalView,
                    c.UploadDate,
                    c.Pages != null ? c.Pages.Select(p => new Page(p.Id, p.ImageUrl, p.LocalImageUrl, p.Size)).ToList() : null
                )).ToList() : null));

        config.NewConfig<Manga, MangaDocument>()
            .Map(dest => dest.Id, src => src.Id.Value)
            .Map(dest => dest.MalID, src => src.MalId)
            .Map(dest => dest.Chapters, src => src.Chapters.Select(c => new ChapterDocument
            {
                Id = c.Id.Value,
                Number = c.Number,
                Link = c.Link,
                ChapterProvider = c.ChapterProvider,
                ChapterProviderIcon = c.ChapterProviderIcon,
                Language = c.Language,
                TotalView = c.TotalView,
                UploadDate = c.UploadDate,
                Pages = c.Pages.Select(p => new PageDocument
                {
                    Id = p.Id,
                    ImageUrl = p.ImageUrl,
                    LocalImageUrl = p.LocalImageUrl,
                    Size = p.Size
                }).ToList()
            }).ToList());
    }
}
