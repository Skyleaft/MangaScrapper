using MangaScrapper.Domain.Aggregates;

namespace MangaScrapper.Application.Common.Abstractions;

public interface IMangaMessagePublisher
{
    Task PublishMangaDeletedAsync(Guid mangaId, string title, CancellationToken ct = default);
    Task PublishChapterDeletedAsync(Guid mangaId, string mangaTitle, Guid chapterId, double chapterNumber, CancellationToken ct = default);
}
