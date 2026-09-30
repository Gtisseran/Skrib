using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Skrib;

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = [];
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

internal enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NoRelease,
    Error
}

internal sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }
    public string Message { get; init; } = "";
    public GitHubRelease? Release { get; init; }
    public GitHubAsset? Asset { get; init; }
    public Version LocalVersion { get; init; } = new(0, 0);
    public Version? RemoteVersion { get; init; }
}

internal static class GitHubUpdateService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly string[] AssetPriority =
    [
        ".msixbundle", ".msix", ".appinstaller", ".exe", ".zip"
    ];

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Skrib", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version GetLocalVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return new Version(v.Major, v.Minor, v.Build, v.Revision);
        }
        catch
        {
            return new Version(1, 0, 5, 0);
        }
    }

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var local = GetLocalVersion();
        try
        {
            using var response = await Http.GetAsync(UpdateConfig.ReleasesApiUrl, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.NoRelease,
                    LocalVersion = local,
                    Message = "no-release"
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Error,
                    LocalVersion = local,
                    Message = $"HTTP {(int)response.StatusCode}"
                };
            }

            var release = JsonSerializer.Deserialize<GitHubRelease>(json);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.NoRelease,
                    LocalVersion = local,
                    Message = "no-release"
                };
            }

            if (!TryParseTag(release.TagName, out var remote))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Error,
                    LocalVersion = local,
                    Release = release,
                    Message = "bad-tag"
                };
            }

            var asset = PickAsset(release);
            if (remote > local)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpdateAvailable,
                    LocalVersion = local,
                    RemoteVersion = remote,
                    Release = release,
                    Asset = asset
                };
            }

            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpToDate,
                LocalVersion = local,
                RemoteVersion = remote,
                Release = release,
                Asset = asset
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Error,
                LocalVersion = local,
                Message = ex.Message
            };
        }
    }

    public static async Task<string> DownloadAssetAsync(GitHubAsset asset, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Skrib-updates");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, asset.Name);

        using var response = await Http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;
            if (total > 0)
            {
                progress?.Report(readTotal / (double)total);
            }
        }

        return target;
    }

    private static GitHubAsset? PickAsset(GitHubRelease release)
    {
        if (release.Assets == null || release.Assets.Length == 0)
        {
            return null;
        }

        foreach (var ext in AssetPriority)
        {
            var match = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        return release.Assets[0];
    }

    internal static bool TryParseTag(string tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var t = tag.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            t = t[1..];
        }

        return Version.TryParse(t, out version!);
    }
}
