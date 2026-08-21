using FluentAssertions;
using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.DomainEvents;
using MangaScrapper.Core.ValueObjects;
using Xunit;

namespace UnitTests.MangaScrapper.Domain;

public class MangaAggregateTests
{
    [Fact]
    public void Create_WithValidArguments_ShouldInstantiateMangaAndRaiseDomainEvent()
    {
        // Arrange
        var title = "One Piece";
        var author = "Eiichiro Oda";
        var type = "Manga";
        var source = MangaSource.Komiku;

        // Act
        var manga = Manga.Create(title, author, type, source: "Komiku");

        // Assert
        manga.Should().NotBeNull();
        manga.Title.Should().Be(title);
        manga.Author.Should().Be(author);
        manga.Type.Should().Be(type);
        manga.DomainEvents.Should().ContainSingle(e => e is MangaCreatedDomainEvent);
    }

    [Fact]
    public void Reconstitute_ShouldHydrateMangaWithoutRaisingDomainEvents()
    {
        // Arrange
        var id = MangaId.New();
        var title = "Naruto";
        var author = "Masashi Kishimoto";
        var type = "Manga";

        // Act
        var manga = Manga.Reconstitute(
            id,
            title,
            author,
            type,
            malId: 20,
            anilistId:30,
            mangaUpdateId:23,
            synonyms: new List<string> { "Alternative Naruto" },
            genres: new List<string> { "Action", "Ninja" },
            categories:new List<string>{"Isekai"},
            description: "A ninja's journey",
            imageUrl: "http://example.com/cover.jpg",
            localImageUrl: "cover.webp",
            thumbnailSize: 1024,
            rating: 8.5,
            popularity: 10,
            members: 5000,
            nsfw:false,
            status: "Completed",
            releaseDate: DateTime.UtcNow.AddYears(-15),
            totalView: 100000,
            createdAt: DateTime.UtcNow.AddYears(-5),
            updatedAt: DateTime.UtcNow,
            url: "http://example.com/manga",
            chapters: null);

        // Assert
        manga.Should().NotBeNull();
        manga.Id.Should().Be(id);
        manga.Title.Should().Be(title);
        manga.Synonyms.Should().ContainSingle().Which.Should().Be("Alternative Naruto");
        manga.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void UpdateFromAnilist_ShouldMergeSynonyms()
    {
        // Arrange
        var manga = Manga.Create("Attack on Titan", "Hajime Isayama", "Manga", "Komiku", synonyms: new List<string> { "AoT" });
        var other = Manga.Create("Shingeki no Kyojin", "Hajime Isayama", "Manga", "Anilist", synonyms: new List<string> { "SNK", "AoT" });

        // Act
        manga.UpdateFromAnilist(other);

        // Assert
        manga.Synonyms.Should().BeEquivalentTo(new List<string> { "AoT", "SNK" });
    }

    [Fact]
    public void UpdateFromAnilist_WhenCollectionsAreNull_ShouldMergeWithoutException()
    {
        // Arrange
        var manga = Manga.Reconstitute(
            MangaId.New(), "Bleach", "Tite Kubo", "Manga", 0, null, null,
            synonyms: null, genres: null, categories: null,
            description: null, imageUrl: null, localImageUrl: null,
            thumbnailSize: 0, rating: null, popularity: 0, members: 0,
            nsfw: false, status: null, releaseDate: null, totalView: 0,
            createdAt: DateTime.UtcNow, updatedAt: DateTime.UtcNow, url: null, chapters: null);

        var other = Manga.Create("Bleach", "Tite Kubo", "Manga", "Anilist",
            synonyms: new List<string> { "Bleach TYBW" },
            genres: new List<string> { "Action", "Supernatural" },
            categories: new List<string> { "Shounen" });

        // Act
        var act = () => manga.UpdateFromAnilist(other);

        // Assert
        act.Should().NotThrow();
        manga.Synonyms.Should().Contain("Bleach TYBW");
        manga.Genres.Should().Contain(new[] { "Action", "Supernatural" });
        manga.Categories.Should().Contain("Shounen");
    }

    [Fact]
    public void UpdateFromAnilist_ShouldExcludeEmptyOrWhitespaceSynonyms()
    {
        // Arrange
        var manga = Manga.Create("Naruto", "Masashi Kishimoto", "Manga", "Komiku", synonyms: new List<string> { "Naruto Shippuden" });
        var other = Manga.Create("Naruto", "Masashi Kishimoto", "Manga", "Anilist", synonyms: new List<string> { "", "   ", "Naruto: Shippuden", null! });

        // Act
        manga.UpdateFromAnilist(other);

        // Assert
        manga.Synonyms.Should().BeEquivalentTo(new List<string> { "Naruto Shippuden", "Naruto: Shippuden" });
        manga.Synonyms.Should().NotContain("");
        manga.Synonyms.Should().NotContain("   ");
    }

    [Fact]
    public void ReconstituteFromAnilist_ShouldExcludeEmptyOrWhitespaceSynonyms()
    {
        // Arrange
        var manga = Manga.Create("Naruto", "Masashi Kishimoto", "Manga", "Komiku", synonyms: new List<string> { "Naruto Shippuden" });
        var anilistMedia = new NovaStack.Contracts.Responses.AnilistMedia(
            Id: 123,
            IdMal: 456,
            Title: new NovaStack.Contracts.Responses.AnilistTitle("Naruto", "Naruto English", "ナルト"),
            Description: "A ninja story",
            CountryOfOrigin: "JP",
            Format: "MANGA",
            Status: "FINISHED",
            Chapters: 700,
            Volumes: 72,
            CoverImage: null,
            AverageScore: 80,
            Popularity: 1000,
            Favorites: 500,
            Genres: new List<string> { "Action" },
            Synonyms: new List<string> { "", "  ", "Naruto: Shippuden" },
            Tags: null,
            StartDate: null,
            Staff: null
        );

        // Act
        manga.ReconstituteFromAnilist(anilistMedia);

        // Assert
        manga.Synonyms.Should().BeEquivalentTo(new List<string> { "Naruto Shippuden", "Naruto: Shippuden" });
        manga.Synonyms.Should().NotContain("");
        manga.Synonyms.Should().NotContain("  ");
    }
}

