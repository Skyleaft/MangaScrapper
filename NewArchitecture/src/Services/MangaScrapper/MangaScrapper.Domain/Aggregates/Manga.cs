using MangaScrapper.Domain.DomainEvents;
using MangaScrapper.Domain.ValueObjects;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Domain.Aggregates;

public class Chapter
{
    public ChapterId Id { get; private set; }
    public double Number { get; private set; }
    public string? Link { get; private set; }
    public string? ChapterProvider { get; private set; }
    public string? ChapterProviderIcon { get; private set; }
    public string Language { get; private set; }
    public int TotalView { get; private set; }
    public DateTime UploadDate { get; private set; }
    public List<Page> Pages { get; private set; }

    public Chapter(
        ChapterId id,
        double number,
        string? link,
        string? chapterProvider,
        string? chapterProviderIcon,
        string language,
        int totalView,
        DateTime uploadDate,
        List<Page>? pages = null)
    {
        Id = id;
        Number = number;
        Link = link;
        ChapterProvider = chapterProvider;
        ChapterProviderIcon = chapterProviderIcon;
        Language = language;
        TotalView = totalView;
        UploadDate = uploadDate;
        Pages = pages ?? [];
    }

    public void AddPage(Page page) => Pages.Add(page);
    public void IncrementView() => TotalView++;
}

public class Page
{
    public Guid Id { get; private set; }
    public string ImageUrl { get; private set; }
    public string? LocalImageUrl { get; private set; }
    public long Size { get; private set; }

    public Page(Guid id, string imageUrl, string? localImageUrl = null, long size = 0)
    {
        Id = id;
        ImageUrl = imageUrl;
        LocalImageUrl = localImageUrl;
        Size = size;
    }

    public void UpdateLocalImage(string localImageUrl, long size)
    {
        LocalImageUrl = localImageUrl;
        Size = size;
    }
}

public class Manga : Entity<MangaId>
{
    public int MalId { get; private set; }
    public int? AnilistId { get; private set; }
    public string Title { get; private set; }
    public string Author { get; private set; }
    public string Type { get; private set; }
    public double? Rating { get; private set; }
    public int Popularity { get; private set; }
    public int Members { get; private set; }
    public List<string> Genres { get; private set; }
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }
    public string? LocalImageUrl { get; private set; }
    public long ThumbnailSize { get; private set; }
    public string? Status { get; private set; }
    public DateTime? ReleaseDate { get; private set; }
    public int TotalView { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public string? Url { get; private set; }
    public List<Chapter> Chapters { get; private set; }

    private Manga(
        MangaId id,
        string title,
        string author,
        string type,
        string source,
        int malId = 0,
        int? anilistId = null,
        List<string>? genres = null,
        string? description = null,
        string? imageUrl = null,
        string? localImageUrl = null,
        long thumbnailSize = 0,
        double? rating = null,
        int popularity = 0,
        int members = 0,
        string? status = null,
        DateTime? releaseDate = null,
        int totalView = 0,
        DateTime? createdAt = null,
        DateTime? updatedAt = null,
        string? url = null,
        List<Chapter>? chapters = null) : base(id)
    {
        Title = Guard.NotNullOrWhiteSpace(title, nameof(title));
        Author = author ?? string.Empty;
        Type = type ?? string.Empty;
        MalId = malId;
        AnilistId = anilistId;
        Genres = genres ?? [];
        Description = description;
        ImageUrl = imageUrl;
        LocalImageUrl = localImageUrl;
        ThumbnailSize = thumbnailSize;
        Rating = rating;
        Popularity = popularity;
        Members = members;
        Status = status;
        ReleaseDate = releaseDate;
        TotalView = totalView;
        CreatedAt = createdAt ?? DateTime.UtcNow;
        UpdatedAt = updatedAt ?? DateTime.UtcNow;
        Url = url;
        Chapters = chapters ?? [];
    }

    public static Manga Create(
        string title,
        string author,
        string type,
        string source,
        int malId = 0,
        int? anilistId = null,
        List<string>? genres = null,
        string? description = null,
        string? imageUrl = null,
        string? url = null)
    {
        var id = MangaId.New();
        var manga = new Manga(
            id, title, author, type, source,
            malId: malId, anilistId: anilistId, genres: genres, description: description, imageUrl: imageUrl, url: url);

        manga.RaiseDomainEvent(new MangaCreatedDomainEvent(id, title, source));
        return manga;
    }

    public static Manga Reconstitute(
        MangaId id,
        string title,
        string author,
        string type,
        int malId,
        int? anilistId,
        List<string>? genres,
        string? description,
        string? imageUrl,
        string? localImageUrl,
        long thumbnailSize,
        double? rating,
        int popularity,
        int members,
        string? status,
        DateTime? releaseDate,
        int totalView,
        DateTime createdAt,
        DateTime updatedAt,
        string? url,
        List<Chapter>? chapters)
    {
        return new Manga(
            id, title, author, type, "Unknown",
            malId, anilistId, genres, description, imageUrl, localImageUrl, thumbnailSize,
            rating, popularity, members, status, releaseDate, totalView,
            createdAt, updatedAt, url, chapters);
    }

    public void UpdateMetadata(
        int malId,
        int? anilistId,
        string author,
        string type,
        List<string> genres,
        string? description,
        double? rating,
        int popularity,
        int members,
        string? status,
        DateTime? releaseDate,
        int totalView)
    {
        MalId = malId;
        AnilistId = anilistId;
        Author = author;
        Type = type;
        Genres = genres ?? [];
        Description = description ?? Description;
        Rating = rating ?? Rating;
        Popularity = popularity > 0 ? popularity : Popularity;
        Members = members > 0 ? members : Members;
        Status = status ?? Status;
        ReleaseDate = releaseDate ?? ReleaseDate;
        TotalView = totalView;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateFromAnilist(AnilistMedia anilistInfo)
    {
        AnilistId = anilistInfo.Id;
        MalId = anilistInfo.IdMal ?? MalId;
        Description = string.IsNullOrEmpty(Description) ? anilistInfo.Description : Description;
        Rating = Rating ?? (anilistInfo.AverageScore.HasValue ? anilistInfo.AverageScore.Value / 10.0 : null);
        Popularity = anilistInfo.Popularity ?? Popularity;
        
        Status = anilistInfo.Status switch
        {
            "FINISHED" => "Completed",
            "RELEASING" => "Ongoing",
            "HIATUS" => "On Hiatus",
            "CANCELLED" => "Discontinued",
            "NOT_YET_RELEASED" => "Upcoming",
            _ => Status ?? "Unknown"
        };

        if (anilistInfo.StartDate?.Year != null)
        {
            int year = anilistInfo.StartDate.Year.Value;
            int month = anilistInfo.StartDate.Month ?? 1;
            int day = anilistInfo.StartDate.Day ?? 1;
            ReleaseDate = ReleaseDate ?? new DateTime(year, month, day);
        }

        if (anilistInfo.Genres != null && anilistInfo.Genres.Any())
        {
            Genres = Genres.Union(anilistInfo.Genres).ToList();
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public void AddChapter(Chapter chapter)
    {
        Chapters.RemoveAll(c => c.Number == chapter.Number && c.ChapterProvider == chapter.ChapterProvider);
        Chapters.Add(chapter);
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ChapterScrapedDomainEvent(Id, chapter.Id, chapter.Number, chapter.ChapterProvider ?? "Unknown"));
    }

    public void DeleteChapter(ChapterId chapterId)
    {
        Chapters.RemoveAll(c => c.Id == chapterId);
        UpdatedAt = DateTime.UtcNow;
    }

    public void IncrementView()
    {
        TotalView++;
    }

    public void UpdateLocalImage(string localImageUrl, long size)
    {
        LocalImageUrl = localImageUrl;
        ThumbnailSize = size;
        UpdatedAt = DateTime.UtcNow;
    }
}
