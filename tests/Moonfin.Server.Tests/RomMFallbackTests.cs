using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Moonfin.Server.Services;
using Moq;
using Xunit;

namespace Moonfin.Server.Tests;

public class RomMFallbackTests
{
    [Fact]
    public async Task SyncWithRomMAsync_WhenRomMUnreachable_DoesNotCrashAndReturnsZeroMatches()
    {
        // Arrange
        var mockLibMgr = new Mock<ILibraryManager>();
        mockLibMgr.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem>());

        var mockLogger = new Mock<ILogger<RomMClient>>();
        var mockFactory = new Mock<IHttpClientFactory>();
        var romMClient = new Mock<RomMClient>(mockFactory.Object, mockLogger.Object);

        // Simulate unreachable RomM server returning empty list
        romMClient.Setup(c => c.GetRomsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RomMGame>());

        var mockSyncLogger = new Mock<ILogger<RomMSyncService>>();
        var syncService = new RomMSyncService(mockLibMgr.Object, romMClient.Object, mockSyncLogger.Object);

        // Act
        var result = await syncService.SyncWithRomMAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalGamesCount);
        Assert.Equal(0, result.MatchedCount);
    }

    [Fact]
    public async Task GetImageStreamAsync_WhenUnreachable_ReturnsNullStream()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<RomMClient>>();
        var mockFactory = new Mock<IHttpClientFactory>();
        var romMClient = new RomMClient(mockFactory.Object, mockLogger.Object);

        // Act
        var (stream, contentType) = await romMClient.GetImageStreamAsync("/nonexistent.jpg");

        // Assert
        Assert.Null(stream);
        Assert.True(string.IsNullOrEmpty(contentType));
    }
}
