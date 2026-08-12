using MangaScrapper.Core.ValueObjects;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Core.Aggregates;

public class UserProgression : Entity<Guid>
{
    public string UserId { get; private set; }
    public MangaId MangaId { get; private set; }
    public ChapterId LastReadChapterId { get; private set; }
    public double LastReadChapterNumber { get; private set; }
    public DateTime LastReadAt { get; private set; }

    private UserProgression(
        Guid id,
        string userId,
        MangaId mangaId,
        ChapterId lastReadChapterId,
        double lastReadChapterNumber,
        DateTime lastReadAt)
        : base(id)
    {
        UserId = Guard.NotNullOrWhiteSpace(userId, nameof(userId));
        MangaId = mangaId;
        LastReadChapterId = lastReadChapterId;
        LastReadChapterNumber = lastReadChapterNumber;
        LastReadAt = lastReadAt;
    }

    public static UserProgression Create(string userId, MangaId mangaId, ChapterId chapterId, double chapterNumber)
    {
        var id = Guid.NewGuid();
        return new UserProgression(id, userId, mangaId, chapterId, chapterNumber, DateTime.UtcNow);
    }

    public static UserProgression Reconstitute(
        Guid id,
        string userId,
        MangaId mangaId,
        ChapterId chapterId,
        double chapterNumber,
        DateTime lastReadAt)
    {
        return new UserProgression(id, userId, mangaId, chapterId, chapterNumber, lastReadAt);
    }

    public void UpdateProgression(ChapterId chapterId, double chapterNumber)
    {
        LastReadChapterId = chapterId;
        LastReadChapterNumber = chapterNumber;
        LastReadAt = DateTime.UtcNow;
    }
}
