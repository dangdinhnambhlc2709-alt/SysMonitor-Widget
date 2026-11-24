using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinHardwareMonitor.Services;

public sealed class UpdateReleaseInfo
{
    public Version Version { get; init; } = new(1, 0, 0);
    public string VersionString { get; init; } = "1.0.0";
    public string TagName { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public long AssetSizeBytes { get; init; }
    public string HtmlUrl { get; init; } = string.Empty;
}

public sealed class UpdateCheckResult
{
    public bool Success { get; init; }
    public bool HasUpdate { get; init; }
    public bool NoReleasesPublished { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public UpdateReleaseInfo? Release { get; init; }
}

public readonly record struct UpdateDownloadProgress(double Percent, long BytesReceived, long TotalBytes);

/// <summary>
/// Bộ kiểm tra & tải cập nhật trực tiếp từ GitHub Releases (chỉ dùng HttpClient & System.Text.Json sẵn có của .NET).
/// </summary>
public sealed class UpdateService
{
    public const string GitHubOwner = "dangdinhnambhlc2709-alt";
    public const string GitHubRepo = "SysMonitor-Widget";
    private const string LatestReleaseApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    public Version CurrentVersion { get; }
    public string CurrentVersionString { get; }

    public UpdateService()
    {
        CurrentVersion = ReadCurrentAppVersion(out string display);
        CurrentVersionString = display;
    }

    private static Version ReadCurrentAppVersion(out string displayString)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (!string.IsNullOrWhiteSpace(infoAttr?.InformationalVersion))
            {
                string raw = infoAttr.InformationalVersion.Split('+')[0].Trim();
                if (TryParseSemanticVersion(raw, out var parsed, out string norm))
                {
                    displayString = norm;
                    return parsed;
                }
            }

            var v = asm.GetName().Version;
            if (v != null)
            {
                var normalized = new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
                displayString = $"{normalized.Major}.{normalized.Minor}.{normalized.Build}";
                return normalized;
            }
        }
        catch { }

        displayString = "1.0.0";
        return new Version(1, 0, 0);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = CreateHttpClient(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(LatestReleaseApiUrl, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult
                {
                    Success = true,
                    HasUpdate = false,
                    NoReleasesPublished = true,
                    StatusMessage = $"Chưa có bản phát hành nào trên GitHub (đang dùng v{CurrentVersionString})."
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    HasUpdate = false,
                    StatusMessage = $"Không thể kiểm tra cập nhật (HTTP {(int)response.StatusCode})."
                };
            }

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagProp)
                ? tagProp.GetString() ?? string.Empty
                : string.Empty;

            if (!TryParseSemanticVersion(tagName, out var remoteVersion, out string remoteVersionString))
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    HasUpdate = false,
                    StatusMessage = $"Không đọc được số phiên bản từ tag '{tagName}'."
                };
            }

            if (remoteVersion <= CurrentVersion)
            {
                return new UpdateCheckResult
                {
                    Success = true,
                    HasUpdate = false,
                    StatusMessage = $"Bạn đang dùng phiên bản mới nhất (v{CurrentVersionString})."
                };
            }

            string releaseName = root.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? tagName
                : tagName;

            string body = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? string.Empty
                : string.Empty;

            string htmlUrl = root.TryGetProperty("html_url", out var htmlProp)
                ? htmlProp.GetString() ?? string.Empty
                : string.Empty;

            // Tìm tệp cài đặt .exe trong mảng assets (ưu tiên tên chứa "Setup")
            string? bestUrl = null;
            string? bestAssetName = null;
            long bestSize = 0;

            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string assetName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? string.Empty : string.Empty;
                    string dlUrl = asset.TryGetProperty("browser_download_url", out var du) ? du.GetString() ?? string.Empty : string.Empty;
                    long size = asset.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long s) ? s : 0;

                    if (!assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(dlUrl))
                        continue;

                    if (bestUrl == null || assetName.Contains("setup", StringComparison.OrdinalIgnoreCase))
                    {
                        bestAssetName = assetName;
                        bestUrl = dlUrl;
                        bestSize = size;
                        if (assetName.Contains("setup", StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(bestUrl))
            {
                return new UpdateCheckResult
                {
                    Success = true,
                    HasUpdate = false,
                    StatusMessage = $"Có bản v{remoteVersionString} trên GitHub nhưng chưa đính kèm tệp cài đặt .exe."
                };
            }

            var info = new UpdateReleaseInfo
            {
                Version = remoteVersion,
                VersionString = remoteVersionString,
                TagName = tagName,
                ReleaseName = string.IsNullOrWhiteSpace(releaseName) ? $"Phiên bản {remoteVersionString}" : releaseName,
                ReleaseNotes = string.IsNullOrWhiteSpace(body) ? "Cải thiện hiệu năng và sửa lỗi." : body.Trim(),
                AssetName = bestAssetName ?? $"SysMonitor-Widget-Setup-{remoteVersionString}.exe",
                DownloadUrl = bestUrl,
                AssetSizeBytes = bestSize,
                HtmlUrl = htmlUrl
            };

            return new UpdateCheckResult
            {
                Success = true,
                HasUpdate = true,
                StatusMessage = $"Phát hiện phiên bản mới v{remoteVersionString}!",
                Release = info
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult
            {
                Success = false,
                HasUpdate = false,
                StatusMessage = "Đã hủy kiểm tra cập nhật."
            };
        }
        catch (HttpRequestException)
        {
            return new UpdateCheckResult
            {
                Success = false,
                HasUpdate = false,
                StatusMessage = "Không thể kết nối GitHub (kiểm tra kết nối mạng)."
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                Success = false,
                HasUpdate = false,
                StatusMessage = $"Lỗi kiểm tra cập nhật: {ex.Message}"
            };
        }
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateReleaseInfo release,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken ct = default)
    {
        string safeFileName = $"SysMonitor-Widget-Setup-{release.VersionString}.exe";
        string tempPath = Path.Combine(Path.GetTempPath(), safeFileName);
        string partialPath = tempPath + ".download";

        using var http = CreateHttpClient(TimeSpan.FromMinutes(10));
        using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? release.AssetSizeBytes;
        await using var sourceStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (var destStream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            byte[] buffer = new byte[81920];
            long received = 0;
            int read;
            var sw = Stopwatch.StartNew();

            while ((read = await sourceStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await destStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;

                if (sw.ElapsedMilliseconds >= 60 || (totalBytes > 0 && received >= totalBytes))
                {
                    sw.Restart();
                    double pct = totalBytes > 0 ? Math.Clamp(received * 100.0 / totalBytes, 0, 100) : 0;
                    progress?.Report(new UpdateDownloadProgress(pct, received, totalBytes));
                }
            }

            progress?.Report(new UpdateDownloadProgress(100, received, Math.Max(totalBytes, received)));
        }

        File.Move(partialPath, tempPath, overwrite: true);
        return tempPath;
    }

    public static bool LaunchSilentUpgradeInstaller(string installerPath, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SILENT /SP- /CLOSEAPPLICATIONS /NORESTART",
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public static bool TryParseSemanticVersion(string? raw, out Version version, out string normalizedText)
    {
        version = new Version(0, 0, 0);
        normalizedText = "0.0.0";

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string cleaned = raw.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[1..];

        // Bỏ hậu tố prerelease/metadata nếu có (ví dụ 1.0.2-beta)
        int dashIdx = cleaned.IndexOfAny(new[] { '-', '+' });
        if (dashIdx > 0)
            cleaned = cleaned[..dashIdx];

        if (!Version.TryParse(cleaned, out var parsed))
            return false;

        int major = Math.Max(0, parsed.Major);
        int minor = Math.Max(0, parsed.Minor);
        int build = Math.Max(0, parsed.Build);
        int rev = parsed.Revision;

        version = rev >= 0 ? new Version(major, minor, build, rev) : new Version(major, minor, build);
        normalizedText = rev > 0 ? $"{major}.{minor}.{build}.{rev}" : $"{major}.{minor}.{build}";
        return true;
    }

    private HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"SysMonitor-Widget/{CurrentVersionString}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
