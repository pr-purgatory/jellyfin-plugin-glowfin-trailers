using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.GlowfinTrailers.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the yt-dlp executable to use. Empty: the plugin downloads the official standalone build
    /// into its data folder and keeps it updated.
    /// </summary>
    public string YtDlpPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the tallest video to fetch, in pixels.</summary>
    public int MaxHeight { get; set; } = 1080;

    /// <summary>Gets or sets how much disk the trailer cache may use, in megabytes. Oldest-played go first.</summary>
    public int CacheLimitMb { get; set; } = 5000;

    /// <summary>Gets or sets extra yt-dlp arguments, one per line (e.g. a cookies file or a JS runtime).</summary>
    public string ExtraArguments { get; set; } = string.Empty;
}
