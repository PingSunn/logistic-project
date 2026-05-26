using System.Text.Json;
using System.Text.Json.Serialization;

namespace CargoFit.Installer;

/// <summary>
/// Minimal projection of the GitHub Releases API response.
/// GET https://api.github.com/repos/PingSunn/cargofit/releases/latest
/// </summary>
internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")]     string Name,
    [property: JsonPropertyName("assets")]   List<GitHubAsset> Assets
);

internal sealed record GitHubAsset(
    [property: JsonPropertyName("name")]                 string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
    [property: JsonPropertyName("size")]                 long Size
);

/// <summary>
/// JSON source-generation context — จำเป็นเพื่อให้ trimming ทำงานได้กับ System.Text.Json
/// </summary>
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(List<GitHubAsset>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class GitHubJsonContext : JsonSerializerContext { }

internal static class GitHubClient
{
    private const string ApiUrl =
        "https://api.github.com/repos/PingSunn/cargofit/releases/latest";

    // GitHub API requires a User-Agent header.
    // Public repo → no auth needed; 60 req/hr anonymous limit is ample for an installer.
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15)
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "CargoFit-Installer/1.0" } }
    };

    /// <summary>
    /// Fetches the latest release from GitHub API.
    /// Throws HttpRequestException or TaskCanceledException on network failure.
    /// </summary>
    internal static async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken ct)
    {
        var json = await Http.GetStringAsync(ApiUrl, ct);
        return JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitHubRelease)
               ?? throw new InvalidDataException("GitHub API returned null");
    }

    /// <summary>
    /// Streams a URL to a temp file, reporting download progress via callback.
    /// Uses ResponseHeadersRead to avoid loading 80-100 MB into RAM at once.
    /// </summary>
    internal static async Task<string> DownloadToTempAsync(
        string url,
        long totalBytes,
        IProgress<(long received, long total)> progress,
        CancellationToken ct)
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"CargoFit-win-Setup-{Guid.NewGuid():N}.exe");

        using var response = await Http.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var dest   = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81_920, useAsync: true);

        var buffer = new byte[81_920];
        long received = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            progress.Report((received, totalBytes));
        }

        return tempPath;
    }
}
