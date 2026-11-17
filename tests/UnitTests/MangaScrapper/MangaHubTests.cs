using FluentAssertions;
using MangaScrapper.Core.Common.Abstractions;
using MangaScrapper.Core.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using NovaStack.Contracts.IntegrationEvents;
using Xunit;

namespace UnitTests.MangaScrapper;

public class MangaHubTests
{
    [Fact]
    public async Task ReportScrapingProgress_ShouldUpdateTrackerAndBroadcastToClients()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<MangaHub>>();
        var mockTracker = new Mock<IScrapingProcessTracker>();
        var mockClients = new Mock<IHubCallerClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        var mockGroupProxy = new Mock<IClientProxy>();

        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroupProxy.Object);

        var hub = new MangaHub(mockLogger.Object, mockTracker.Object)
        {
            Clients = mockClients.Object
        };

        var mangaId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var payload = new ChapterScrapingProgressPayload
        {
            MangaId = mangaId,
            MangaTitle = "One Piece",
            ChapterId = chapterId,
            ChapterNumber = 1050,
            DownloadedPages = 10,
            TotalPages = 20,
            Percent = 50,
            Status = "InProgress",
            OccurredOn = DateTime.UtcNow
        };

        // Act
        await hub.ReportScrapingProgress(payload);

        // Assert
        mockTracker.Verify(t => t.TrackProgress(It.Is<ChapterScrapingProgressIntegrationEvent>(e =>
            e.MangaId == mangaId &&
            e.ChapterId == chapterId &&
            e.DownloadedPages == 10 &&
            e.Percent == 50)), Times.Once);

        var expectedGroup = MangaHub.GetMangaGroupName(mangaId);
        mockClients.Verify(c => c.Group(expectedGroup), Times.Once);
        mockClients.Verify(c => c.All, Times.Once);
        mockGroupProxy.Verify(p => p.SendCoreAsync("ChapterScrapingProgress", It.Is<object[]>(args => args.Length == 1 && args[0] == payload), It.IsAny<CancellationToken>()), Times.Once);
        mockClientProxy.Verify(p => p.SendCoreAsync("ChapterScrapingProgress", It.Is<object[]>(args => args.Length == 1 && args[0] == payload), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportChapterPagesScraped_ShouldBroadcastToGroupAndAllClients()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<MangaHub>>();
        var mockClients = new Mock<IHubCallerClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        var mockGroupProxy = new Mock<IClientProxy>();

        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroupProxy.Object);

        var hub = new MangaHub(mockLogger.Object)
        {
            Clients = mockClients.Object
        };

        var mangaId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var payload = new ChapterPagesScrapedPayload
        {
            MangaId = mangaId,
            MangaTitle = "One Piece",
            ChapterId = chapterId,
            ChapterNumber = 1050,
            PageCount = 20,
            OccurredOn = DateTime.UtcNow
        };

        // Act
        await hub.ReportChapterPagesScraped(payload);

        // Assert
        var expectedGroup = MangaHub.GetMangaGroupName(mangaId);
        mockClients.Verify(c => c.Group(expectedGroup), Times.Once);
        mockClients.Verify(c => c.All, Times.Once);
        mockGroupProxy.Verify(p => p.SendCoreAsync("ChaptersUpdated", It.Is<object[]>(args => args.Length == 1 && args[0] == payload), It.IsAny<CancellationToken>()), Times.Once);
        mockClientProxy.Verify(p => p.SendCoreAsync("ChaptersUpdated", It.Is<object[]>(args => args.Length == 1 && args[0] == payload), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportScrapingProgress_WhenCallerIsUnauthorized_ShouldThrowHubException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<MangaHub>>();
        var mockCallerContext = new Mock<HubCallerContext>();
        var anonymousUser = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
        mockCallerContext.Setup(c => c.User).Returns(anonymousUser);

        var hub = new MangaHub(mockLogger.Object)
        {
            Context = mockCallerContext.Object
        };

        var payload = new ChapterScrapingProgressPayload
        {
            MangaId = Guid.NewGuid(),
            MangaTitle = "One Piece",
            ChapterId = Guid.NewGuid(),
            ChapterNumber = 1050
        };

        // Act & Assert
        var act = () => hub.ReportScrapingProgress(payload);
        await act.Should().ThrowAsync<HubException>().WithMessage("*Unauthorized*");
    }

    [Fact]
    public async Task JoinUserGroup_WhenCallerDoesNotMatchUserId_ShouldNotJoinGroup()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<MangaHub>>();
        var mockCallerContext = new Mock<HubCallerContext>();
        var mockGroups = new Mock<IGroupManager>();

        var identity = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "user-123")
        }, "TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        mockCallerContext.Setup(c => c.User).Returns(principal);
        mockCallerContext.Setup(c => c.UserIdentifier).Returns("user-123");
        mockCallerContext.Setup(c => c.ConnectionId).Returns("conn-1");

        var hub = new MangaHub(mockLogger.Object)
        {
            Context = mockCallerContext.Object,
            Groups = mockGroups.Object
        };

        // Act - try to join another user's group
        await hub.JoinUserGroup("victim-user-456");

        // Assert - should not add to group
        mockGroups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JoinUserGroup_WhenCallerMatchesUserId_ShouldJoinGroup()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<MangaHub>>();
        var mockCallerContext = new Mock<HubCallerContext>();
        var mockGroups = new Mock<IGroupManager>();

        var identity = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "user-123")
        }, "TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        mockCallerContext.Setup(c => c.User).Returns(principal);
        mockCallerContext.Setup(c => c.UserIdentifier).Returns("user-123");
        mockCallerContext.Setup(c => c.ConnectionId).Returns("conn-1");

        var hub = new MangaHub(mockLogger.Object)
        {
            Context = mockCallerContext.Object,
            Groups = mockGroups.Object
        };

        // Act - join own user group
        await hub.JoinUserGroup("user-123");

        // Assert - should add to group
        var expectedGroup = MangaHub.GetUserGroupName("user-123");
        mockGroups.Verify(g => g.AddToGroupAsync("conn-1", expectedGroup, It.IsAny<CancellationToken>()), Times.Once);
    }
}
