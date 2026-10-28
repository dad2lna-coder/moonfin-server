using System.Security.Cryptography;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Moonfin.Server.Services;

/// <summary>
/// Data structure for item-to-RomM mapping saved on disk.
/// </summary>
public class RomMMappingEntry
{
    public Guid ItemId { get; set; }
    public string? ItemName { get; set; }
    public string Path { get; set; } = string.Empty;
    public long RomMId { get; set; }
    public string? RomMFsName { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public string? PathCoverLarge { get; set; }
    public string? PathCoverSmall { get; set; }
    public DateTime LastSynced { get; set; } = DateTime.UtcNow;
}

public class RomMSyncResult
{
    public int TotalGamesCount { get; set; }
    public int MatchedCount { get; set; }
    public List<RomMMappingEntry> Mappings { get; set; } = new();
}

public class RomMSyncService
{
    private readonly ILibraryManager _libraryManager;
    private readonly RomMClient _romMClient;
    private readonly ILogger<RomMSyncService> _logger;
    private readonly object _lock = new();
    private Dictionary<Guid, RomMMappingEntry> _mappings = new();

    public RomMSyncService(ILibraryManager libraryManager, RomMClient romMClient, ILogger<RomMSyncService> logger)
    {
        _libraryManager = libraryManager;
        _romMClient = romMClient;
        _logger = logger;
        LoadMappings();
    }

    private string MappingFilePath => Path.Combine(MoonfinPlugin.Instance?.DataFolderPath ?? string.Empty, "romm_mapping.json");

    private void LoadMappings()
    {
        lock (_lock)
        {
            try
            {
                var filePath = MappingFilePath;
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var list = JsonConvert.DeserializeObject<List<RomMMappingEntry>>(json);
                    if (list != null)
                    {
                        _mappings = list.ToDictionary(m => m.ItemId, m => m);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load RomM mappings from disk");
            }
        }
    }

    private void SaveMappings()
    {
        lock (_lock)
        {
            try
            {
                var folder = MoonfinPlugin.Instance?.DataFolderPath;
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                    var json = JsonConvert.SerializeObject(_mappings.Values.ToList(), Formatting.Indented);
                    File.WriteAllText(MappingFilePath, json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save RomM mappings to disk");
            }
        }
    }

    public RomMMappingEntry? GetMapping(Guid itemId)
    {
        lock (_lock)
        {
            return _mappings.TryGetValue(itemId, out var entry) ? entry : null;
        }
    }

    public List<RomMMappingEntry> GetAllMappings()
    {
        lock (_lock)
        {
            return _mappings.Values.ToList();
        }
    }

    public async Task<RomMSyncResult> SyncWithRomMAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting RomM sync...");

        var roms = await _romMClient.GetRomsAsync(null, cancellationToken).ConfigureAwait(false);
        if (roms.Count == 0)
        {
            _logger.LogWarning("RomM returned no ROMs or RomM is unreachable");
            return new RomMSyncResult { TotalGamesCount = 0, MatchedCount = _mappings.Count, Mappings = GetAllMappings() };
        }

        // Index RomM ROMs
        var nameMap = new Dictionary<string, RomMGame>(StringComparer.OrdinalIgnoreCase);
        var crcMap = new Dictionary<string, RomMGame>(StringComparer.OrdinalIgnoreCase);
        var md5Map = new Dictionary<string, RomMGame>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in roms)
        {
            if (!string.IsNullOrWhiteSpace(r.FsName))
            {
                nameMap[r.FsName.Trim()] = r;
            }
            if (!string.IsNullOrWhiteSpace(r.CrcHash))
            {
                crcMap[r.CrcHash.Trim()] = r;
            }
            if (!string.IsNullOrWhiteSpace(r.Md5Hash))
            {
                md5Map[r.Md5Hash.Trim()] = r;
            }
        }

        // Query game items in Jellyfin library
        var query = new InternalItemsQuery
        {
            Recursive = true
        };

        var items = _libraryManager.GetItemList(query)
            .Where(i => !i.IsFolder && !string.IsNullOrEmpty(i.Path) && File.Exists(i.Path))
            .ToList();

        int matchedCount = 0;
        var newMappings = new Dictionary<Guid, RomMMappingEntry>();

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Path)) continue;

            var fileName = Path.GetFileName(item.Path);
            RomMGame? matchedRom = null;
            string matchReason = "";

            // 1. Match by ROM file name
            if (!string.IsNullOrEmpty(fileName) && nameMap.TryGetValue(fileName.Trim(), out matchedRom))
            {
                matchReason = "FileName";
            }

            // 2. Fall back to CRC / MD5 hash if file exists on disk and not matched yet
            if (matchedRom == null && File.Exists(item.Path))
            {
                try
                {
                    // Calculate hashes
                    var (crc, md5) = ComputeHashes(item.Path);
                    if (!string.IsNullOrEmpty(crc) && crcMap.TryGetValue(crc, out matchedRom))
                    {
                        matchReason = "CRC32";
                    }
                    else if (!string.IsNullOrEmpty(md5) && md5Map.TryGetValue(md5, out matchedRom))
                    {
                        matchReason = "MD5";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed computing hashes for file {Path}", item.Path);
                }
            }

            if (matchedRom != null)
            {
                matchedCount++;
                newMappings[item.Id] = new RomMMappingEntry
                {
                    ItemId = item.Id,
                    ItemName = item.Name,
                    Path = item.Path,
                    RomMId = matchedRom.Id,
                    RomMFsName = matchedRom.FsName,
                    MatchReason = matchReason,
                    PathCoverLarge = matchedRom.PathCoverLarge,
                    PathCoverSmall = matchedRom.PathCoverSmall,
                    LastSynced = DateTime.UtcNow
                };
            }
        }

        lock (_lock)
        {
            _mappings = newMappings;
            SaveMappings();
        }

        _logger.LogInformation("RomM sync complete: matched {MatchedCount} of {TotalCount} games", matchedCount, items.Count);

        return new RomMSyncResult
        {
            TotalGamesCount = items.Count,
            MatchedCount = matchedCount,
            Mappings = newMappings.Values.ToList()
        };
    }

    public static (string Crc32, string Md5) ComputeHashes(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        uint crc = 0xffffffff;
        using var md5 = MD5.Create();

        byte[] buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < bytesRead; i++)
            {
                byte b = buffer[i];
                crc = Crc32Table[(crc ^ b) & 0xff] ^ (crc >> 8);
            }
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
        }
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        crc ^= 0xffffffff;
        var crcString = crc.ToString("x8");
        var md5String = Convert.ToHexString(md5.Hash ?? Array.Empty<byte>()).ToLowerInvariant();

        return (crcString, md5String);
    }

    private static readonly uint[] Crc32Table = InitializeCrc32Table();

    private static uint[] InitializeCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint entry = i;
            for (int j = 0; j < 8; j++)
            {
                if ((entry & 1) == 1)
                    entry = (entry >> 1) ^ 0xedb88320;
                else
                    entry >>= 1;
            }
            table[i] = entry;
        }
        return table;
    }
}
