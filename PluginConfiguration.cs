using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;
using Newtonsoft.Json;

namespace Moonfin.Server;

/// <summary>
/// Admin-level plugin configuration for Moonfin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    private string? _romMPassword;

    /// <summary>
    /// Enable settings sync across Moonfin clients.
    /// </summary>
    public bool EnableSettingsSync { get; set; } = true;

    /// <summary>
    /// Enable Jellyseerr integration for all users.
    /// </summary>
    public bool JellyseerrEnabled { get; set; } = false;

    /// <summary>
    /// Jellyseerr server URL (admin-configured, public-facing).
    /// </summary>
    public string? JellyseerrUrl { get; set; }

    /// <summary>
    /// RomM server URL.
    /// </summary>
    public string? RomMUrl { get; set; }

    /// <summary>
    /// RomM HTTP basic auth username.
    /// </summary>
    public string? RomMUsername { get; set; }

    /// <summary>
    /// RomM HTTP basic auth password (stored safely in plugin config XML).
    /// </summary>
    [JsonIgnore]
    public string? RomMPassword
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_romMPassword) && MoonfinPlugin.Instance?.Configuration != null)
            {
                return MoonfinPlugin.Instance.Configuration._romMPassword;
            }
            return _romMPassword;
        }
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value != "********")
            {
                _romMPassword = value;
            }
        }
    }

    /// <summary>
    /// JSON wrapper for RomMPassword to ensure credentials are never returned in API responses.
    /// </summary>
    [JsonProperty("RomMPassword")]
    public string? RomMPasswordForJson
    {
        get => null; // Never return password in JSON API responses
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value != "********")
            {
                _romMPassword = value;
            }
        }
    }

    /// <summary>
    /// Art source selection: "cache", "romm_first", "romm_only".
    /// </summary>
    public string RomMArtSource { get; set; } = "romm_first";

    /// <summary>
    /// Gets the effective Jellyseerr URL for server-to-server communication.
    /// </summary>
    public string? GetEffectiveJellyseerrUrl()
    {
        return JellyseerrUrl?.TrimEnd('/');
    }

    /// <summary>
    /// Gets the effective RomM URL for server-to-server communication.
    /// </summary>
    public string? GetEffectiveRomMUrl()
    {
        return RomMUrl?.TrimEnd('/');
    }

    /// <summary>
    /// Returns true if RomM is configured with a valid URL.
    /// </summary>
    public bool IsRomMConfigured()
    {
        return !string.IsNullOrWhiteSpace(RomMUrl);
    }
}
