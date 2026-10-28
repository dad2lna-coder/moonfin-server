using System.Net.Http.Headers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moonfin.Server.Services;

namespace Moonfin.Server.Api;

/// <summary>
/// Controller for proxying RomM cover art images and managing cache cleanup.
/// </summary>
[ApiController]
[Route("Moonfin/RomM")]
public class RomMImageController : ControllerBase
{
    private readonly RomMSyncService _syncService;
    private readonly RomMClient _romMClient;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<RomMImageController> _logger;

    public RomMImageController(
        RomMSyncService syncService,
        RomMClient romMClient,
        ILibraryManager libraryManager,
        ILogger<RomMImageController> logger)
    {
        _syncService = syncService;
        _romMClient = romMClient;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Serves / proxies cover images from RomM directly.
    /// Uses path_cover_large for detail views ("primary") and path_cover_small for grids ("thumb").
    /// Quietly falls back to local item primary image stream if RomM is down or unmapped.
    /// </summary>
    [HttpGet("Images/{itemId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetImage([FromRoute] Guid itemId, [FromQuery] string type = "primary", CancellationToken cancellationToken = default)
    {
        var config = MoonfinPlugin.Instance?.Configuration;
        var artSource = config?.RomMArtSource ?? "romm_first";

        if (artSource != "cache" && config?.IsRomMConfigured() == true)
        {
            var mapping = _syncService.GetMapping(itemId);
            if (mapping != null)
            {
                var relPath = (type.Equals("thumb", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(mapping.PathCoverSmall))
                    ? mapping.PathCoverSmall
                    : mapping.PathCoverLarge;

                if (string.IsNullOrEmpty(relPath))
                {
                    relPath = mapping.PathCoverLarge;
                }

                if (!string.IsNullOrEmpty(relPath))
                {
                    var (stream, contentType) = await _romMClient.GetImageStreamAsync(relPath, cancellationToken).ConfigureAwait(false);
                    if (stream != null)
                    {
                        Response.Headers["Cache-Control"] = "public, max-age=86400"; // Short HTTP caching header
                        return File(stream, contentType);
                    }
                }
            }
        }

        if (artSource == "romm_only")
        {
            return NotFound();
        }

        // Quiet fallback: serve existing local Primary image if available
        var item = _libraryManager.GetItemById(itemId);
        if (item != null && item.HasImage(MediaBrowser.Model.Entities.ImageType.Primary))
        {
            var imagePath = item.GetImagePath(MediaBrowser.Model.Entities.ImageType.Primary);
            if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
            {
                Response.Headers["Cache-Control"] = "public, max-age=86400";
                var ext = System.IO.Path.GetExtension(imagePath).ToLowerInvariant();
                var contentType = ext == ".png" ? "image/png" : "image/jpeg";
                return PhysicalFile(imagePath, contentType);
            }
        }

        return NotFound();
    }

    /// <summary>
    /// Manual endpoint to trigger sync with RomM.
    /// </summary>
    [HttpPost("Sync")]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<ActionResult<RomMSyncResult>> TriggerSync(CancellationToken cancellationToken = default)
    {
        var result = await _syncService.SyncWithRomMAsync(cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Previews space freed and file count if cached images for mapped RomM items are deleted.
    /// </summary>
    [HttpGet("ClearCache/Preview")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult<ClearCacheReport> PreviewClearCache()
    {
        var report = CalculateCacheCleanup(deleteFiles: false);
        return Ok(report);
    }

    /// <summary>
    /// Deletes cached primary images for items currently mapped to RomM and returns space freed.
    /// </summary>
    [HttpPost("ClearCache/Execute")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult<ClearCacheReport> ExecuteClearCache()
    {
        var report = CalculateCacheCleanup(deleteFiles: true);
        return Ok(report);
    }

    private ClearCacheReport CalculateCacheCleanup(bool deleteFiles)
    {
        var mappings = _syncService.GetAllMappings();
        long bytesFreed = 0;
        int fileCount = 0;

        foreach (var mapping in mappings)
        {
            var item = _libraryManager.GetItemById(mapping.ItemId);
            if (item == null) continue;

            // Check Primary image
            if (item.HasImage(MediaBrowser.Model.Entities.ImageType.Primary))
            {
                var imagePath = item.GetImagePath(MediaBrowser.Model.Entities.ImageType.Primary);
                if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
                {
                    try
                    {
                        var fileInfo = new System.IO.FileInfo(imagePath);
                        bytesFreed += fileInfo.Length;
                        fileCount++;

                        if (deleteFiles)
                        {
                            System.IO.File.Delete(imagePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to inspect or delete cached image at {Path}", imagePath);
                    }
                }
            }
        }

        return new ClearCacheReport
        {
            FileCount = fileCount,
            BytesFreed = bytesFreed
        };
    }
}

public class ClearCacheReport
{
    public int FileCount { get; set; }
    public long BytesFreed { get; set; }
}
