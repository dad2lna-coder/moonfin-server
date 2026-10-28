using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Moonfin.Server.Services;

/// <summary>
/// Scheduled task to periodically sync game library mappings with RomM server.
/// </summary>
public class RomMSyncTask : IScheduledTask
{
    private readonly RomMSyncService _syncService;
    private readonly ILogger<RomMSyncTask> _logger;

    public RomMSyncTask(RomMSyncService syncService, ILogger<RomMSyncTask> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Sync Games with RomM Server";

    /// <inheritdoc />
    public string Key => "Moonfin.RomMSync";

    /// <inheritdoc />
    public string Description => "Matches library game files with RomM entries by file name and CRC/MD5 hashes.";

    /// <inheritdoc />
    public string Category => "Moonfin";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerDaily,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            }
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RomM scheduled sync task started");
        progress.Report(10);

        var result = await _syncService.SyncWithRomMAsync(cancellationToken).ConfigureAwait(false);

        progress.Report(100);
        _logger.LogInformation("RomM scheduled sync task finished: matched {Matched}/{Total}", result.MatchedCount, result.TotalGamesCount);
    }
}
