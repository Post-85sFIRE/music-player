using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using MusicPlayer.Core.Interfaces;

namespace MusicPlayer.Desktop;

/// <summary>
/// 版本更新检查：对比本地版本与远端最新版本，发现新版本时弹窗提示前往下载；
/// 任何异常（网络不可达 / 解析失败 / 无权限等）一律<b>静默忽略</b>，绝不打扰用户。
/// 远端支持两种格式：GitHub Releases（含 tag_name + html_url）或自定义 JSON（含 version + url/downloadUrl）。
/// </summary>
public static class VersionChecker
{
    public static async Task CheckAsync(ISettingsService settings, bool manual = false)
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(settings.Settings.UpdateUrl)
                ? AppVersion.DefaultUpdateUrl
                : settings.Settings.UpdateUrl;
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MusicPlayer-Desktop");
            var json = await http.GetStringAsync(url).ConfigureAwait(false);
            var (latest, downloadUrl) = Parse(json);

            if (latest is null)
            {
                if (manual) ShowInfo("当前已是最新版本（" + AppVersion.Current + "）");
                return;
            }

            if (Compare(AppVersion.Current, latest) < 0)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    var r = System.Windows.MessageBox.Show(
                        $"发现新版本 {latest}\n当前版本 {AppVersion.Current}\n\n是否前往下载？",
                        "更新可用", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (r == MessageBoxResult.Yes && !string.IsNullOrEmpty(downloadUrl))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(downloadUrl) { UseShellExecute = true });
                });
            }
            else if (manual)
            {
                ShowInfo("当前已是最新版本（" + AppVersion.Current + "）");
            }
        }
        catch
        {
            // 失败静默忽略：更新检查绝不影响正常使用。
        }
    }

    private static void ShowInfo(string msg)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            System.Windows.MessageBox.Show(msg, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information));
    }

    private static (string? Latest, string? Url) Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("tag_name", out var tag)) // GitHub Releases
            {
                var ver = tag.GetString();
                var url = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
                return (ver, url);
            }
            if (root.TryGetProperty("version", out var v)) // 自定义 JSON
            {
                var ver = v.GetString();
                string? url = null;
                if (root.TryGetProperty("url", out var u)) url = u.GetString();
                else if (root.TryGetProperty("downloadUrl", out var d)) url = d.GetString();
                return (ver, url);
            }
        }
        catch
        {
            // 解析失败 → 视为无结果，静默。
        }
        return (null, null);
    }

    /// <summary>版本比较：剥离前导 v，按 System.Version 比较；任一无法解析则视为相等（不提示更新）。返回 &lt;0 表示 latest 更新。</summary>
    private static int Compare(string current, string latest)
    {
        try { return ParseVersion(current).CompareTo(ParseVersion(latest)); }
        catch { return 0; }
    }

    private static Version ParseVersion(string s)
    {
        s = s.Trim();
        if (s.Length > 0 && (s[0] is 'v' or 'V')) s = s[1..];
        return new Version(s);
    }
}
