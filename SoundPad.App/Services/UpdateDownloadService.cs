using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SoundPad.App.Services;

public static class UpdateDownloadService
{
    private static readonly HttpClient _http;

    static UpdateDownloadService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SoundPad/1.2");
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to %TEMP%\SoundPad\Updates\<paramref name="fileName"/>,
    /// streaming in 80 KB chunks and reporting (bytesDownloaded, totalBytes) progress.
    /// totalBytes may be -1 when Content-Length is absent (indeterminate progress).
    /// Throws <see cref="OperationCanceledException"/> when <paramref name="ct"/> fires.
    /// Throws <see cref="IOException"/> when the finished file is missing or empty.
    /// </summary>
    public static async Task<string> DownloadAsync(
        string url,
        string fileName,
        IProgress<(long Downloaded, long Total)>? progress,
        CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SoundPad", "Updates");
        Directory.CreateDirectory(dir);

        // Strip any directory components from the API-supplied name to prevent path traversal.
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeName))
            safeName = "SoundPad-Setup.exe";
        var localPath = Path.Combine(dir, safeName);

        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1L;

        await using var stream = await response.Content
            .ReadAsStreamAsync(ct)
            .ConfigureAwait(false);

        await using var file = new FileStream(
            localPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var  buffer     = new byte[81920];
        long downloaded = 0;
        int  read;

        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            progress?.Report((downloaded, total));
        }

        await file.FlushAsync(ct).ConfigureAwait(false);

        // Verify the finished file is present and non-empty.
        var info = new FileInfo(localPath);
        if (!info.Exists || info.Length == 0)
            throw new IOException("Downloaded file is missing or empty.");

        return localPath;
    }
}
