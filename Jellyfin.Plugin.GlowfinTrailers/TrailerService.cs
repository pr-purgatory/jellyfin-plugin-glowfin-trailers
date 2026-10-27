using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GlowfinTrailers;

/// <summary>A title's online trailer: its YouTube video id and the name the metadata gave it.</summary>
public sealed record RemoteTrailer(string VideoId, string? Name);

/// <summary>
/// Picks an item's YouTube trailer from its <c>RemoteTrailers</c> (filled by TMDB metadata), fetches it once with
/// yt-dlp as an MP4 Apple TV plays directly (H.264 + AAC, moov first), and keeps it in a size-limited cache.
/// </summary>
public sealed partial class TrailerService(
    IApplicationPaths applicationPaths,
    IMediaEncoder mediaEncoder,
    YtDlp ytDlp,
    ILogger<TrailerService> logger)
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _fetches = new();

    private string CacheFolder => Path.Combine(applicationPaths.CachePath, "glowfin-trailers");

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    public static partial Regex VideoIdPattern();

    /// <summary>The trailer to play: one called a trailer before teasers, clips and promos, else the first.</summary>
    public static RemoteTrailer? FindTrailer(BaseItem item)
    {
        var candidates = (item.RemoteTrailers ?? [])
            .Select((trailer, index) => (Id: YouTubeId(trailer.Url), trailer.Name, Index: index))
            .Where(candidate => candidate.Id is not null)
            .DistinctBy(candidate => candidate.Id)
            .OrderBy(candidate => Rank(candidate.Name))
            .ThenBy(candidate => candidate.Index);
        return candidates.Select(candidate => new RemoteTrailer(candidate.Id!, candidate.Name)).FirstOrDefault();
    }

    private static int Rank(string? name)
    {
        var lower = name?.ToLowerInvariant() ?? string.Empty;
        if (new[] { "teaser", "clip", "promo", "featurette", "spot", "behind" }.Any(lower.Contains))
        {
            return 2;
        }

        return lower.Contains("official", StringComparison.Ordinal) && lower.Contains("trailer", StringComparison.Ordinal) ? 0 : 1;
    }

    /// <summary>
    /// The video id in a YouTube link: <c>youtube.com/watch?v=</c>, <c>youtu.be/</c>, <c>/embed/</c>, <c>/shorts/</c>,
    /// or Kodi's <c>plugin://plugin.video.youtube/?videoid=</c> from NFO files.
    /// </summary>
    public static string? YouTubeId(string? link)
    {
        if (string.IsNullOrWhiteSpace(link) || !Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? candidate = host switch
        {
            "youtu.be" => path.FirstOrDefault(),
            "plugin.video.youtube" => query["videoid"] ?? query["video_id"],
            _ when host.EndsWith("youtube.com", StringComparison.Ordinal) || host.EndsWith("youtube-nocookie.com", StringComparison.Ordinal) =>
                query["v"] ?? (path.Length >= 2 && path[0] is "embed" or "shorts" or "v" or "live" ? path[1] : null),
            _ => null,
        };
        return candidate is not null && VideoIdPattern().IsMatch(candidate) ? candidate : null;
    }

    /// <summary>The cached file for a video, if it has been fetched.</summary>
    public string? CachedPath(string videoId)
    {
        var path = Path.Combine(CacheFolder, videoId + ".mp4");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// The trailer's file, fetching it first if needed. Requests for the same video share one download, which
    /// isn't cancelled when one caller gives up (the next press of Play Trailer would only start it again).
    /// </summary>
    public async Task<string> FetchAsync(string videoId, CancellationToken cancellationToken)
    {
        if (CachedPath(videoId) is { } cached)
        {
            File.SetLastAccessTimeUtc(cached, DateTime.UtcNow);
            return cached;
        }

        var fetch = _fetches.GetOrAdd(videoId, id => new Lazy<Task<string>>(() => DownloadAsync(id)));
        try
        {
            return await fetch.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (fetch.Value.IsCompleted)
            {
                _fetches.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(videoId, fetch));
            }
        }
    }

    private async Task<string> DownloadAsync(string videoId)
    {
        using var timeout = new CancellationTokenSource(FetchTimeout);
        var configuration = Plugin.Instance!.Configuration;
        var executable = await ytDlp.EnsureAsync(timeout.Token).ConfigureAwait(false);
        Directory.CreateDirectory(CacheFolder);

        var height = Math.Clamp(configuration.MaxHeight, 144, 4320);
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        string[] arguments =
        [
            "--no-playlist", "--no-progress", "--no-mtime", "--no-cache-dir",
            "--ffmpeg-location", mediaEncoder.EncoderPath,
            // Apple TV plays H.264 + AAC in MP4 without the server transcoding it.
            "-f", $"bv*[vcodec^=avc1][height<={height}]+ba[acodec^=mp4a]/b[vcodec^=avc1][acodec^=mp4a][height<={height}]/bv*[vcodec^=avc1]+ba[acodec^=mp4a]",
            "--merge-output-format", "mp4",
            // Index at the front, so playback starts before the player has read the whole file.
            "--postprocessor-args", "Merger:-movflags +faststart",
            "-o", Path.Combine(CacheFolder, videoId + ".%(ext)s"),
        ];
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var line in configuration.ExtraArguments.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            start.ArgumentList.Add(line);
        }

        start.ArgumentList.Add("--");
        start.ArgumentList.Add($"https://www.youtube.com/watch?v={videoId}");

        logger.LogInformation("Fetching trailer {VideoId}", videoId);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        _ = process.StandardOutput.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            RemovePartials(videoId);
            throw new TrailerFetchException("Fetching the trailer took too long.");
        }

        var path = CachedPath(videoId);
        if (process.ExitCode != 0 || path is null)
        {
            var message = (await errors.ConfigureAwait(false)).Trim();
            logger.LogWarning("yt-dlp couldn't fetch {VideoId} (exit {Code}): {Errors}", videoId, process.ExitCode, message);
            RemovePartials(videoId);
            throw new TrailerFetchException(message.Split('\n').LastOrDefault(line => line.StartsWith("ERROR", StringComparison.Ordinal)) ?? "yt-dlp failed.");
        }

        File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        Trim(keep: path);
        return path;
    }

    private void RemovePartials(string videoId)
    {
        foreach (var file in Directory.EnumerateFiles(CacheFolder, videoId + ".*").Where(file => !file.EndsWith(videoId + ".mp4", StringComparison.Ordinal)))
        {
            File.Delete(file);
        }
    }

    /// <summary>Deletes the trailers played longest ago until the cache fits its limit.</summary>
    private void Trim(string keep)
    {
        var limit = Math.Max(100, Plugin.Instance!.Configuration.CacheLimitMb) * 1024L * 1024L;
        var files = new DirectoryInfo(CacheFolder).GetFiles("*.mp4").OrderBy(file => file.LastAccessTimeUtc).ToList();
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (total <= limit)
            {
                break;
            }

            if (file.FullName == keep)
            {
                continue;
            }

            total -= file.Length;
            file.Delete();
        }
    }
}

/// <summary>yt-dlp couldn't fetch a trailer; the message is yt-dlp's own error.</summary>
public sealed class TrailerFetchException(string message) : Exception(message);
