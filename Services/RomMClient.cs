using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Moonfin.Server.Services;

/// <summary>
/// Model representing a platform from RomM API (GET /api/platforms).
/// </summary>
public class RomMPlatform
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("slug")]
    public string? Slug { get; set; }

    [JsonProperty("fs_slug")]
    public string? FsSlug { get; set; }
}

/// <summary>
/// Model representing a ROM from RomM API (GET /api/roms?platform_id=X).
/// </summary>
public class RomMGame
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("platform_id")]
    public long PlatformId { get; set; }

    [JsonProperty("fs_name")]
    public string? FsName { get; set; }

    [JsonProperty("crc_hash")]
    public string? CrcHash { get; set; }

    [JsonProperty("md5_hash")]
    public string? Md5Hash { get; set; }

    [JsonProperty("sha1_hash")]
    public string? Sha1Hash { get; set; }

    [JsonProperty("path_cover_large")]
    public string? PathCoverLarge { get; set; }

    [JsonProperty("path_cover_small")]
    public string? PathCoverSmall { get; set; }
}

/// <summary>
/// Service to communicate with self-hosted RomM server.
/// Credentials are used for Basic Auth header and never logged.
/// </summary>
public class RomMClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RomMClient> _logger;

    public RomMClient(IHttpClientFactory httpClientFactory, ILogger<RomMClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpClient CreateClient()
    {
        var config = MoonfinPlugin.Instance?.Configuration;
        var client = _httpClientFactory.CreateClient("RomMClient");

        if (config != null && !string.IsNullOrWhiteSpace(config.RomMUsername) && !string.IsNullOrWhiteSpace(config.RomMPassword))
        {
            var authBytes = Encoding.UTF8.GetBytes($"{config.RomMUsername}:{config.RomMPassword}");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        }

        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    /// <summary>
    /// Gets all platforms from RomM.
    /// </summary>
    public virtual async Task<List<RomMPlatform>> GetPlatformsAsync(CancellationToken cancellationToken = default)
    {
        var config = MoonfinPlugin.Instance?.Configuration;
        if (config == null || !config.IsRomMConfigured())
        {
            return new List<RomMPlatform>();
        }

        try
        {
            var baseUrl = config.GetEffectiveRomMUrl();
            var requestUrl = $"{baseUrl}/api/platforms";

            using var client = CreateClient();
            var response = await client.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("RomM GetPlatforms returned HTTP {StatusCode}", response.StatusCode);
                return new List<RomMPlatform>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<List<RomMPlatform>>(json) ?? new List<RomMPlatform>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching platforms from RomM server");
            return new List<RomMPlatform>();
        }
    }

    /// <summary>
    /// Gets all ROMs for a specific platform ID (or all ROMs if platform_id is null).
    /// </summary>
    public virtual async Task<List<RomMGame>> GetRomsAsync(long? platformId = null, CancellationToken cancellationToken = default)
    {
        var config = MoonfinPlugin.Instance?.Configuration;
        if (config == null || !config.IsRomMConfigured())
        {
            return new List<RomMGame>();
        }

        try
        {
            var baseUrl = config.GetEffectiveRomMUrl();
            var requestUrl = platformId.HasValue
                ? $"{baseUrl}/api/roms?platform_id={platformId.Value}"
                : $"{baseUrl}/api/roms";

            using var client = CreateClient();
            var response = await client.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("RomM GetRoms returned HTTP {StatusCode}", response.StatusCode);
                return new List<RomMGame>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<List<RomMGame>>(json) ?? new List<RomMGame>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching ROMs from RomM server");
            return new List<RomMGame>();
        }
    }

    /// <summary>
    /// Fetches an image stream from RomM by relative path (e.g. /assets/romm/resources/...).
    /// </summary>
    public virtual async Task<(Stream? Stream, string ContentType)> GetImageStreamAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var config = MoonfinPlugin.Instance?.Configuration;
        if (config == null || !config.IsRomMConfigured() || string.IsNullOrWhiteSpace(relativePath))
        {
            return (null, string.Empty);
        }

        try
        {
            var baseUrl = config.GetEffectiveRomMUrl();
            var cleanPath = relativePath.StartsWith("/") ? relativePath : "/" + relativePath;
            var requestUrl = $"{baseUrl}{cleanPath}";

            using var client = CreateClient();
            var response = await client.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("RomM GetImageStream returned HTTP {StatusCode} for path {Path}", response.StatusCode, cleanPath);
                return (null, string.Empty);
            }

            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var memoryStream = new MemoryStream();
            await responseStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            memoryStream.Position = 0;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            return (memoryStream, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching image from RomM server path {Path}", relativePath);
            return (null, string.Empty);
        }
    }
}
