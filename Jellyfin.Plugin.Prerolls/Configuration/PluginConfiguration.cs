using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Prerolls.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the folder containing the preroll videos.
    /// </summary>
    public string PrerollFolder { get; set; } = string.Empty;
}
