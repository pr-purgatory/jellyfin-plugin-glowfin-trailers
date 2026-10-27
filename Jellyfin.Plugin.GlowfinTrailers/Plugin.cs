using Jellyfin.Plugin.GlowfinTrailers.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.GlowfinTrailers;

/// <summary>
/// Glowfin Trailers: fetches a title's online (YouTube) trailer onto the server, so Glowfin can play it in its
/// own player. See README.md.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Glowfin Trailers";

    public override Guid Id => Guid.Parse("c00de493-890e-4581-9220-eb1c881b2e14");

    public override string Description =>
        "Fetches movie and show trailers from their online trailer links, so Glowfin can play them in its own player.";

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
        },
    ];
}
