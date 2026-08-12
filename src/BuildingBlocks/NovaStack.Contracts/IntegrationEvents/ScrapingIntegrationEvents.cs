namespace NovaStack.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published when a manga chapter needs its pages scraped.
/// Consumed by the Scrapper.Worker via RabbitMQ.
/// </summary>
public sealed record ScrapChapterPagesIntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
    public string EventType { get; init; } = nameof(ScrapChapterPagesIntegrationEvent);

    public Guid MangaId { get; init; }
    public string MangaTitle { get; init; } = string.Empty;
    public double ChapterNumber { get; init; }
    public string ChapterId { get; init; } = string.Empty;

    /// <summary>The provider key used to resolve the correct <c>IScrapperService</c> (e.g. "komiku", "kiryuu").</summary>
    public string Provider { get; init; } = string.Empty;

    public ScrapChapterPagesIntegrationEvent() { }

    public ScrapChapterPagesIntegrationEvent(
        Guid mangaId,
        string mangaTitle,
        double chapterNumber,
        string chapterId,
        string provider)
    {
        MangaId = mangaId;
        MangaTitle = mangaTitle;
        ChapterNumber = chapterNumber;
        ChapterId = chapterId;
        Provider = provider;
    }
}

/// <summary>
/// Integration event published when a manga is deleted.
/// Consumed by the Scrapper.Worker via RabbitMQ — handles file cleanup + removal from all stores.
/// </summary>
public sealed record DeleteMangaIntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
    public string EventType { get; init; } = nameof(DeleteMangaIntegrationEvent);

    public Guid MangaId { get; init; }
    public string MangaTitle { get; init; } = string.Empty;

    public DeleteMangaIntegrationEvent() { }

    public DeleteMangaIntegrationEvent(Guid mangaId, string mangaTitle)
    {
        MangaId = mangaId;
        MangaTitle = mangaTitle;
    }
}

/// <summary>
/// Integration event published when a chapter within a manga is deleted.
/// Consumed by the Scrapper.Worker via RabbitMQ — handles file cleanup + removal from MongoDB.
/// </summary>
public sealed record DeleteChapterIntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
    public string EventType { get; init; } = nameof(DeleteChapterIntegrationEvent);

    public Guid MangaId { get; init; }
    public string MangaTitle { get; init; } = string.Empty;
    public Guid ChapterId { get; init; }
    public double ChapterNumber { get; init; }

    public DeleteChapterIntegrationEvent() { }

    public DeleteChapterIntegrationEvent(Guid mangaId, string mangaTitle, Guid chapterId, double chapterNumber)
    {
        MangaId = mangaId;
        MangaTitle = mangaTitle;
        ChapterId = chapterId;
        ChapterNumber = chapterNumber;
    }
}
