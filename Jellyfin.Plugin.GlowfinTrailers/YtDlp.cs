using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GlowfinTrailers;

/// <summary>
/// Finds yt-dlp: the configured path, or the official standalone build, downloaded into the plugin's data folder on
/// first use and self-updated (<c>yt-dlp -U</c>) once a week, since YouTube changes often break older versions.
/// </summary>
public sealed class YtDlp(IHttpClientFactory httpClientFactory, ILogger<YtDlp> logger)
{
    private static readonly TimeSpan UpdateEvery = TimeSpan.FromDays(7);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<string> EnsureAsync(CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin not loaded.");
        if (!string.IsNullOrWhiteSpace(plugin.Configuration.YtDlpPath))
        {
            return plugin.Configuration.YtDlpPath;
        }

        var path = Path.Combine(plugin.DataFolderPath, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                await DownloadAsync(path, cancellationToken).ConfigureAwait(false);
            }
            else if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > UpdateEvery)
            {
                await UpdateAsync(path, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }

        return path;
    }

    /// <summary>The release asset for this server's OS and CPU.</summary>
    private static string AssetName()
    {
        var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsWindows())
        {
            return arm ? "yt-dlp_arm64.exe" : "yt-dlp.exe";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "yt-dlp_macos";
        }

        return arm ? "yt-dlp_linux_aarch64" : "yt-dlp_linux";
    }

    private async Task DownloadAsync(string path, CancellationToken cancellationToken)
    {
        var url = $"https://github.com/yt-dlp/yt-dlp/releases/latest/download/{AssetName()}";
        logger.LogInformation("Downloading yt-dlp from {Url}", url);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".download";
        using (var client = httpClientFactory.CreateClient())
        await using (var source = await client.GetStreamAsync(new Uri(url), cancellationToken).ConfigureAwait(false))
        await using (var file = File.Create(temporary))
        {
            await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private async Task UpdateAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "-U")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            logger.LogInformation("yt-dlp update: {Output}", output.Trim());
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Couldn't update yt-dlp; using the current version");
        }

        // Checked again in a week either way.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
    }
}
