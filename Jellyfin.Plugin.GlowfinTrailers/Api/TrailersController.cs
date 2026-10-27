using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.GlowfinTrailers.Api;

/// <summary>The trailer Glowfin plays for an item, and the video file itself.</summary>
public sealed record TrailerInfo(string Name, string Path);

/// <summary>Endpoints Glowfin calls. All need a signed-in user (header or <c>ApiKey</c> query).</summary>
[ApiController]
[Authorize]
[Route("GlowfinTrailers")]
public sealed class TrailersController(ILibraryManager libraryManager, IUserManager userManager, TrailerService trailers) : ControllerBase
{
    /// <summary>Tells Glowfin the plugin is installed.</summary>
    [HttpGet("Info")]
    public ActionResult<object> Info() =>
        new { Version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "1.0.0.0" };

    /// <summary>
    /// Fetches the item's online trailer if it isn't cached yet (this can take a while), then says where to play it.
    /// 404 when the item has no YouTube trailer link; 502 with yt-dlp's error when fetching fails.
    /// </summary>
    [HttpGet("Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<TrailerInfo>> Item([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        var item = FindItem(itemId);
        if (item is null || TrailerService.FindTrailer(item) is not { } trailer)
        {
            return NotFound();
        }

        try
        {
            await trailers.FetchAsync(trailer.VideoId, cancellationToken).ConfigureAwait(false);
        }
        catch (TrailerFetchException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, exception.Message);
        }

        return new TrailerInfo(trailer.Name ?? "Trailer", $"GlowfinTrailers/Videos/{trailer.VideoId}");
    }

    /// <summary>A fetched trailer, as MP4 with range requests (what AVPlayer asks for).</summary>
    [HttpGet("Videos/{videoId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Video([FromRoute] string videoId)
    {
        if (!TrailerService.VideoIdPattern().IsMatch(videoId) || trailers.CachedPath(videoId) is not { } path)
        {
            return NotFound();
        }

        return PhysicalFile(path, "video/mp4", enableRangeProcessing: true);
    }

    /// <summary>The item, if the signed-in user can see it.</summary>
    private BaseItem? FindItem(Guid itemId)
    {
        var claim = User.Claims.FirstOrDefault(claim => claim.Type == "Jellyfin-UserId")?.Value;
        var user = Guid.TryParse(claim, out var userId) ? userManager.GetUserById(userId) : null;
        return user is null ? null : libraryManager.GetItemById<BaseItem>(itemId, user);
    }
}
