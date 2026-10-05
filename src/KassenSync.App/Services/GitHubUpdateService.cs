using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using KassenSync.App.Models;

namespace KassenSync.App.Services;

public sealed class GitHubUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/tojollinor/KassenSync/releases/latest";
    private const string SetupAssetName = "KassenSync-Setup.exe";
    private const string ChecksumAssetName = "KassenSync-Setup.exe.sha256";
    private readonly HttpClient _httpClient;

    public GitHubUpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KassenSync", AppVersion.Display));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!TryParseVersion(tag, out var releaseVersion))
            throw new InvalidOperationException($"Ungültige Release-Version: {tag}");

        if (releaseVersion <= AppVersion.Current)
            return null;

        string? setupUrl = null;
        string? checksumUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.Equals(name, SetupAssetName, StringComparison.OrdinalIgnoreCase)) setupUrl = url;
            if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase)) checksumUrl = url;
        }

        if (string.IsNullOrWhiteSpace(setupUrl))
            throw new InvalidOperationException($"Im GitHub-Release fehlt '{SetupAssetName}'.");

        var releaseName = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        return new UpdateInfo(releaseVersion, tag, setupUrl, checksumUrl, releaseName);
    }

    public async Task<string> DownloadAndVerifyAsync(UpdateInfo update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KassenSync",
            "Updates");
        Directory.CreateDirectory(updateDirectory);

        var setupPath = Path.Combine(updateDirectory, $"KassenSync-Setup-{update.Version}.exe");
        var tempPath = setupPath + ".download";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        using (var response = await _httpClient.GetAsync(update.SetupDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            var buffer = new byte[1024 * 1024];
            long copied = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                if (total is > 0)
                    progress?.Report((int)Math.Clamp(copied * 100L / total.Value, 0, 100));
            }
            await output.FlushAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(update.ChecksumDownloadUrl))
        {
            var checksumText = await _httpClient.GetStringAsync(update.ChecksumDownloadUrl, cancellationToken);
            var expected = checksumText.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(expected))
                throw new InvalidOperationException("Die SHA-256-Prüfsumme des Updates ist leer.");

            await using var hashStream = File.OpenRead(tempPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Die SHA-256-Prüfsumme des heruntergeladenen Updates stimmt nicht.");
        }

        File.Move(tempPath, setupPath, true);
        progress?.Report(100);
        return setupPath;
    }

    public void LaunchInstaller(string setupPath)
    {
        var startInfo = new ProcessStartInfo(setupPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE"
        };
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Der Installer konnte nicht gestartet werden.");
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var dash = value.IndexOf('-');
        if (dash >= 0) value = value[..dash];
        return Version.TryParse(value, out version!);
    }
}
