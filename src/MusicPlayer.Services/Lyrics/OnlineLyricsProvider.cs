using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Core.Lyrics;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Services.Lyrics;

/// <summary>
/// 网络歌词来源（兜底，默认关闭）。按 艺术家 + 标题 查询公开歌词 API（api.lyrics.ovh，无需密钥）。
/// 返回纯文本（可能含 LRC 时间轴，也可能不含）；无时间轴时按行展示但不做高亮跟随。
/// </summary>
public class OnlineLyricsProvider : ILyricsProvider
{
    private readonly HttpClient _http = new();

    public string Name => "Online";

    public async Task<LyricsResult?> GetAsync(LyricsContext ctx, CancellationToken ct)
    {
        var (artist, title) = ExtractQuery(ctx);
        if (string.IsNullOrWhiteSpace(title)) return null;

        try
        {
            var url = string.IsNullOrWhiteSpace(artist)
                ? $"https://api.lyrics.ovh/v1/{System.Uri.EscapeDataString(title)}"
                : $"https://api.lyrics.ovh/v1/{System.Uri.EscapeDataString(artist)}/{System.Uri.EscapeDataString(title)}";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("lyrics", out var el)) return null;
            var lyric = el.GetString();
            if (string.IsNullOrWhiteSpace(lyric)) return null;

            var lines = LrcParser.Parse(lyric);
            if (lines.Length == 0)
            {
                lines = lyric.Split('\n')
                    .Select(t => new LyricLine { Time = TimeSpan.Zero, Text = t.TrimEnd('\r') })
                    .Where(l => l.Text.Length > 0)
                    .ToArray();
            }

            return new LyricsResult { Lines = lines, Raw = lyric, Source = "Online" };
        }
        catch
        {
            return null;
        }
    }

    private static (string? Artist, string? Title) ExtractQuery(LyricsContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.SearchQuery))
        {
            var q = ctx.SearchQuery.Trim();
            var idx = q.IndexOf(" - ", System.StringComparison.Ordinal);
            if (idx > 0)
                return (q.Substring(0, idx).Trim(), q.Substring(idx + 3).Trim());
            return (null, q);
        }
        return (ctx.Track.Artist.Trim(), ctx.Track.Title.Trim());
    }
}
