using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SoundPad.App.Services;

public enum UpdateStatus { UpToDate, UpdateAvailable, NetworkError }

public sealed class UpdateCheckResult
{
    public UpdateStatus Status             { get; init; }
    public string?      LatestTag          { get; init; }
    public string?      ReleaseUrl         { get; init; }
    public string?      ReleaseName        { get; init; }
    public string?      ReleaseBody        { get; init; }
    public string?      InstallerAssetUrl  { get; init; }  // browser_download_url of matched .exe
    public string?      InstallerAssetName { get; init; }  // original filename of that asset
}

public static class UpdateCheckService
{
    private const string ApiUrl = "https://api.github.com/repos/ihabg/soundpad/releases/latest";

    private static readonly HttpClient _http;

    static UpdateCheckService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SoundPad/1.2");
    }

    public static async Task<UpdateCheckResult> CheckAsync(string currentVersion)
    {
        try
        {
            var json = await _http.GetStringAsync(ApiUrl).ConfigureAwait(false);

            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagProp))
                return new UpdateCheckResult { Status = UpdateStatus.NetworkError };

            var tag  = tagProp.GetString() ?? "";
            var url  = root.TryGetProperty("html_url", out var urlProp)  ? urlProp.GetString()  ?? "" : "";
            var name = root.TryGetProperty("name",     out var nameProp) ? nameProp.GetString() ?? "" : tag;
            var body = root.TryGetProperty("body",     out var bodyProp) ? bodyProp.GetString()        : null;

            // Find the installer asset: first entry whose name matches SoundPad-Setup-*.exe.
            string? assetUrl  = null;
            string? assetName = null;
            if (root.TryGetProperty("assets", out var assetsProp) &&
                assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var n = asset.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                    if (n.StartsWith("SoundPad-Setup-", StringComparison.OrdinalIgnoreCase) &&
                        n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        assetUrl  = asset.TryGetProperty("browser_download_url", out var up)
                            ? up.GetString() : null;
                        assetName = n;
                        break;
                    }
                }
            }

            var latestStr  = tag.TrimStart('v');
            var currentStr = currentVersion.TrimStart('v');

            if (!Version.TryParse(latestStr, out var latest) ||
                !Version.TryParse(currentStr, out var current))
            {
                // Unparseable tag — treat as up to date rather than showing a spurious banner.
                StartupLogger.Log($"Update check: could not parse versions (tag={tag}, current={currentVersion})");
                return new UpdateCheckResult { Status = UpdateStatus.UpToDate, LatestTag = tag, ReleaseUrl = url };
            }

            var status = latest > current ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate;
            StartupLogger.Log($"Update check: latest={latestStr}, current={currentStr}, status={status}");

            return new UpdateCheckResult
            {
                Status             = status,
                LatestTag          = tag,
                ReleaseUrl         = url,
                ReleaseName        = name,
                ReleaseBody        = body,
                InstallerAssetUrl  = assetUrl,
                InstallerAssetName = assetName,
            };
        }
        catch (Exception ex)
        {
            StartupLogger.Log($"Update check failed: {ex.GetType().Name}: {ex.Message}");
            return new UpdateCheckResult { Status = UpdateStatus.NetworkError };
        }
    }
}
