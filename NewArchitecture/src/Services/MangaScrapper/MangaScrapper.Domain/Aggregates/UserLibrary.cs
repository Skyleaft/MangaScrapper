using MangaScrapper.Domain.DomainEvents;
using MangaScrapper.Domain.ValueObjects;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Domain.Aggregates;

public class UserLibrary : Entity<Guid>
{
    public string UserId { get; private set; }
    public MangaId MangaId { get; private set; }
    public DateTime AddedAt { get; private set; }

    private UserLibrary(Guid id, string userId, MangaId mangaId, DateTime addedAt)
        : base(id)
    {
        UserId = Guard.NotNullOrWhiteSpace(userId, nameof(userId));
        MangaId = mangaId;
        AddedAt = addedAt;
    }

    public static UserLibrary Create(string userId, MangaId mangaId)
    {
        var id = Guid.NewGuid();
        var userLibrary = new UserLibrary(id, userId, mangaId, DateTime.UtcNow);
        userLibrary.RaiseDomainEvent(new UserLibraryUpdatedDomainEvent(id, userId, mangaId, "Added"));
        return userLibrary;
    }

    public static UserLibrary Reconstitute(Guid id, string userId, MangaId mangaId, DateTime addedAt)
    {
        return new UserLibrary(id, userId, mangaId, addedAt);
    }
}
