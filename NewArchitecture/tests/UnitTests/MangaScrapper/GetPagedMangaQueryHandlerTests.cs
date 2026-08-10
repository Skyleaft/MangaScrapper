using FluentAssertions;
using MangaScrapper.Application.Features.MangaData.GetPagedManga;
using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using Moq;
using NovaStack.SharedKernel.Common;
using Xunit;

namespace UnitTests.MangaScrapper.Application;

public class GetPagedMangaQueryHandlerTests
{
    private readonly Mock<IMangaRepository> _mangaRepositoryMock;
    private readonly Mock<IMangaSearchRepository> _searchRepositoryMock;
    private readonly GetPagedMangaQueryHandler _handler;

    public GetPagedMangaQueryHandlerTests()
    {
        _mangaRepositoryMock = new Mock<IMangaRepository>();
        _searchRepositoryMock = new Mock<IMangaSearchRepository>();
        _handler = new GetPagedMangaQueryHandler(_mangaRepositoryMock.Object,_searchRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenMangaExists_ShouldReturnPagedMangaList()
    {
        // Arrange
        var mangaList = new List<Manga>
        {
            Manga.Create("Bleach", "Tite Kubo", "Manga", source: "Komiku"),
            Manga.Create("Dragon Ball", "Akira Toriyama", "Manga", source: "Komiku")
        };

        var pagedList = new PagedList<Manga>(mangaList, page: 1, pageSize: 10, totalCount: 2);

        _mangaRepositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),It.IsAny<string?>(),It.IsAny<int>(),It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedList);

        var query = new GetPagedMangaQuery(Page: 1, PageSize: 10);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
    }
}
