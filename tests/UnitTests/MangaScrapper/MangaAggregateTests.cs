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
}
