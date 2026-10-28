using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Moonfin.Server.Services;
using Moq;
using Xunit;

namespace Moonfin.Server.Tests;

public class TestGameItem : BaseItem
{
    public TestGameItem(Guid id, string name, string path)
    {
        Id = id;
        Name = name;
        Path = path;
    }
}

public class RomMMatchingTests
{
    [Fact]
    public async Task Matching_ByFileName_Succeeds()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var tempFileName = Path.GetFileName(tempFile);

        try
        {
            var testItemId = Guid.NewGuid();
            var testItem = new TestGameItem(testItemId, "Super Mario Bros", tempFile);

            var mockLibMgr = new Mock<ILibraryManager>();
            mockLibMgr.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new List<BaseItem> { testItem });

            var mockLogger = new Mock<ILogger<RomMClient>>();
            var mockFactory = new Mock<IHttpClientFactory>();
            var romMClient = new Mock<RomMClient>(mockFactory.Object, mockLogger.Object);

            var roms = new List<RomMGame>
            {
                new RomMGame
                {
                    Id = 12345,
                    Name = "Super Mario Bros",
                    FsName = tempFileName,
                    PathCoverLarge = "/assets/romm/resources/mario_large.jpg",
                    PathCoverSmall = "/assets/romm/resources/mario_small.jpg"
                }
            };

            romMClient.Setup(c => c.GetRomsAsync(null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(roms);

            var mockSyncLogger = new Mock<ILogger<RomMSyncService>>();
            var syncService = new RomMSyncService(mockLibMgr.Object, romMClient.Object, mockSyncLogger.Object);

            // Act
            var result = await syncService.SyncWithRomMAsync();

            // Assert
            Assert.Equal(1, result.TotalGamesCount);
            Assert.Equal(1, result.MatchedCount);

            var mapping = syncService.GetMapping(testItemId);
            Assert.NotNull(mapping);
            Assert.Equal(12345, mapping.RomMId);
            Assert.Equal("FileName", mapping.MatchReason);
            Assert.Equal("/assets/romm/resources/mario_large.jpg", mapping.PathCoverLarge);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task Matching_ByCrc32AndMd5Hash_Succeeds()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var dummyData = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCD, 0xEF };
        await File.WriteAllBytesAsync(tempFile, dummyData);

        try
        {
            var (crc, md5) = RomMSyncService.ComputeHashes(tempFile);

            var testItemId = Guid.NewGuid();
            var testItem = new TestGameItem(testItemId, "Test Game Hash Match", tempFile);

            var mockLibMgr = new Mock<ILibraryManager>();
            mockLibMgr.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new List<BaseItem> { testItem });

            var mockLogger = new Mock<ILogger<RomMClient>>();
            var mockFactory = new Mock<IHttpClientFactory>();
            var romMClient = new Mock<RomMClient>(mockFactory.Object, mockLogger.Object);

            var roms = new List<RomMGame>
            {
                new RomMGame
                {
                    Id = 999,
                    Name = "Test Game Hash Match",
                    FsName = "different_filename.sfc",
                    CrcHash = crc,
                    Md5Hash = md5,
                    PathCoverLarge = "/assets/romm/resources/cover_large.jpg"
                }
            };

            romMClient.Setup(c => c.GetRomsAsync(null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(roms);

            var mockSyncLogger = new Mock<ILogger<RomMSyncService>>();
            var syncService = new RomMSyncService(mockLibMgr.Object, romMClient.Object, mockSyncLogger.Object);

            // Act
            var result = await syncService.SyncWithRomMAsync();

            // Assert
            Assert.Equal(1, result.MatchedCount);
            var mapping = syncService.GetMapping(testItemId);
            Assert.NotNull(mapping);
            Assert.Equal(999, mapping.RomMId);
            Assert.Equal("CRC32", mapping.MatchReason);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
