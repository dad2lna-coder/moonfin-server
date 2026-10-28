using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Drawing;
using Microsoft.Extensions.Logging;

namespace Moonfin.Server.Services;

/// <summary>
/// Dynamic image provider for Jellyfin library items mapped to RomM.
/// Implements IDynamicImageProvider so Jellyfin serves cover art on-the-fly.
/// </summary>
public class RomMImageProvider : IDynamicImageProvider
{
    private readonly RomMSyncService _syncService;
    private readonly RomMClient _romMClient;
    private readonly ILogger<RomMImageProvider> _logger;

    public RomMImageProvider(RomMSyncService syncService, RomMClient romMClient, ILogger<RomMImageProvider> logger)
    {
        _syncService = syncService;
        _romMClient = romMClient;
        _logger = logger;
    }

    public string Name => "RomM Box Art";

    public bool Supports(BaseItem item) => true;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new[] { ImageType.Primary, ImageType.Thumb };
    }

    public async Task<DynamicImageResponse> GetImage(BaseItem item, ImageType type, CancellationToken cancellationToken)
    {
        var config = MoonfinPlugin.Instance?.Configuration;
        if (config == null || config.RomMArtSource == "cache" || !config.IsRomMConfigured())
        {
            return new DynamicImageResponse { HasImage = false };
        }

        var mapping = _syncService.GetMapping(item.Id);
        if (mapping == null)
        {
            return new DynamicImageResponse { HasImage = false };
        }

        var relPath = (type == ImageType.Thumb && !string.IsNullOrEmpty(mapping.PathCoverSmall))
            ? mapping.PathCoverSmall
            : mapping.PathCoverLarge;

        if (string.IsNullOrEmpty(relPath))
        {
            relPath = mapping.PathCoverLarge;
        }

        if (string.IsNullOrEmpty(relPath))
        {
            return new DynamicImageResponse { HasImage = false };
        }

        var (stream, contentType) = await _romMClient.GetImageStreamAsync(relPath, cancellationToken).ConfigureAwait(false);
        if (stream == null)
        {
            return new DynamicImageResponse { HasImage = false };
        }

        var format = contentType.Contains("png", StringComparison.OrdinalIgnoreCase)
            ? ImageFormat.Png
            : ImageFormat.Jpg;

        return new DynamicImageResponse
        {
            HasImage = true,
            Stream = stream,
            Format = format
        };
    }
}
