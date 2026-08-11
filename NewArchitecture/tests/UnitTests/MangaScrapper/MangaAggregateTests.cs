using FluentAssertions;
using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.DomainEvents;
using MangaScrapper.Domain.ValueObjects;
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
            genres: new List<string> { "Action", "Ninja" },
            description: "A ninja's journey",
            imageUrl: "http://example.com/cover.jpg",
            localImageUrl: "cover.webp",
            thumbnailSize: 1024,
            rating: 8.5,
            popularity: 10,
            members: 5000,
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
        manga.DomainEvents.Should().BeEmpty();
    }
}
