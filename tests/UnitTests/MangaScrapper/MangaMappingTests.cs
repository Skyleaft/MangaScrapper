using FluentAssertions;
using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Common.Mappings;
using MangaScrapper.Core.Persistence.Documents;
using MangaScrapper.Core.ValueObjects;
using Mapster;
using Xunit;

namespace UnitTests.MangaScrapper;

public class MangaMappingTests
{
    public MangaMappingTests()
    {
        TypeAdapterConfig.GlobalSettings.Scan(typeof(MangaMappingConfig).Assembly);
    }

    [Fact]
    public void MangaDocument_To_Manga_ShouldPreserveGuidId()
    {
        // Arrange
        var expectedId = Guid.CreateVersion7();
        var doc = new MangaDocument
        {
            Id = expectedId,
            Title = "Test Manga",
            Author = "Test Author",
            Type = "Manga",
            MalID = 123,
            Status = "Ongoing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Chapters = new List<ChapterDocument>
            {
                new ChapterDocument
                {
                    Id = Guid.CreateVersion7(),
                    Number = 1.0,
                    Language = "en",
                    UploadDate = DateTime.UtcNow
                }
            }
        };

        // Act
        var manga = doc.Adapt<Manga>();

        // Assert
        manga.Should().NotBeNull();
        manga.Id.Value.Should().Be(expectedId);
        manga.Id.Value.Should().NotBe(Guid.Empty);
    }
}
