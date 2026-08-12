using MangaScrapper.Core.Common.Abstractions;
using NovaStack.Contracts.IntegrationEvents;
using NovaStack.Infrastructure.Messaging;

namespace MangaScrapper.Core.Services;

public sealed class MangaMessagePublisher(IEventBus eventBus) : IMangaMessagePublisher
{
    public async Task PublishMangaDeletedAsync(Guid mangaId, string title, CancellationToken ct = default)
    {
        await eventBus.PublishAsync(
            new DeleteMangaIntegrationEvent(mangaId, title),
            "delete-manga",
            ct);
    }

    public async Task PublishChapterDeletedAsync(Guid mangaId, string mangaTitle, Guid chapterId, double chapterNumber, CancellationToken ct = default)
    {
        await eventBus.PublishAsync(
            new DeleteChapterIntegrationEvent(mangaId, mangaTitle, chapterId, chapterNumber),
            "delete-chapter",
            ct);
    }
}
